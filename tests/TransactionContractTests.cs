using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.Assemblies;
using NXOpen.UF;

// Direct exporter tests still go through the same driver-owned context boundary.
internal sealed class TransactionTestScope : IDisposable
{
    private readonly TransactionContext _context;
    private readonly List<ITransactionHandler> _handlers = new List<ITransactionHandler>();
    public TransactionTestScope(Session session, Component target, string output, string name)
    {
        _context = new TransactionContext(session, target, (Part)target.Prototype, output, target.DisplayName, name);
    }
    public void Process(ITransactionHandler handler)
    {
        if (!_handlers.Contains(handler)) _handlers.Add(handler);
        _context.Execute(handler);
    }
    public void Dispose()
    {
        try { _context.Close(); }
        finally { foreach (var handler in _handlers) handler.Finish(); }
    }
    public static void Run(ITransactionHandler handler, Component comp, Session session, string output, string name)
    { using (var scope = new TransactionTestScope(session, comp, output, name)) scope.Process(handler); }
}

internal static class TransactionContractTests
{
    private static int _checks;
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception("Transaction contract: " + message); ++_checks; }
    private sealed class Probe : ITransactionHandler
    {
        public Action<TransactionContext> Work;
        public bool FailFinish;
        public int Calls, Finishes;
        public void Process(TransactionContext context) { ++Calls; if (Work != null) Work(context); }
        public void Finish() { ++Finishes; if (FailFinish) throw new IOException("finish failure"); }
    }
    private static Session Setup(out Part root, out Component first, out Component second)
    {
        UFSession.Instance.Abort.Flag = UFSession.Instance.Abort.Fail = false;
        TraversalStopWindow.OnOpen = null;
        Session session = ImageViewTests.Setup(out root, out first);
        first.DisplayName = "first";
        second = root.ComponentAssembly.RootComponent.Children[1];
        second.DisplayName = "second";
        second.Prototype = session.Add("second", PartLoadState.PartiallyLoaded);
        return session;
    }
    private static TraversalConfig Config(string output)
    { return new TraversalConfig { filePath = output }; }

    public static int Run()
    {
        string output = Path.Combine(Path.GetTempPath(), "nx-transactions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            RegistrationAndLifecycle(output);
            SharedServicesAndIsolation(output);
        }
        finally { Directory.Delete(output, true); }
        return _checks;
    }
    private static void RegistrationAndLifecycle(string output)
    {
        Part root; Component first, second;
        Session session = Setup(out root, out first, out second);
        // Metadata handlers need neither camera validation nor geometry preparation.
        root.Views.ActiveOverride = new View[0];
        var order = new List<string>();
        TransactionContext previous = null;
        var a = new Probe { Work = c => {
            order.Add("a:" + c.Id); previous = c;
            Check(c.TargetPart == c.Target.Prototype && c.Session == session, "Driver supplies consistent target/session");
            Check(c.DescriptiveName == c.Id + "_" + c.Name && c.FileStem.StartsWith("part_"), "Stable shared naming metadata");
            c.Log("custom metadata transaction");
        }};
        var b = new Probe { Work = c => { order.Add("b:" + c.Id); Check(ReferenceEquals(c, previous), "All handlers share one target context"); }};
        AssemblyTraverser.Run(Config(output), new ITransactionHandler[] { a, b });
        Check(string.Join(",", order) == "a:first,b:first,a:second,b:second", "Registration order is independent of traversal");
        Check(a.Finishes == 1 && b.Finishes == 1, "Finish once per handler/run, not per node");
        Check(!session.ListingWindow.Lines.Exists(x => x.Contains("[Export context]")), "Metadata-only plugin never creates a display mask");
        bool closed = false;
        try { previous.CheckCancellation(); } catch (ObjectDisposedException) { closed = true; }
        Check(closed, "Borrowed services cannot be reused after target unload");

        session = Setup(out root, out first, out second);
        a = new Probe { Work = c => { throw new IOException("custom operation failed"); }, FailFinish = true };
        b = new Probe();
        AssemblyTraverser.Run(Config(output), new ITransactionHandler[] { a, b });
        Check(a.Calls == 2 && b.Calls == 2, "Ordinary custom failure does not skip another handler or node");
        Check(a.Finishes == 1 && b.Finishes == 1, "Finish failure does not skip remaining Finish calls");

        session = Setup(out root, out first, out second);
        a = new Probe { Work = c => { throw new OperationCanceledException(); }};
        b = new Probe();
        AssemblyTraverser.Run(Config(output), new ITransactionHandler[] { a, b });
        Check(a.Calls == 1 && b.Calls == 0, "Custom cancellation skips remaining handlers and nodes");
        Check(a.Finishes == 1 && b.Finishes == 1, "Canceled and unstarted registered handlers are finalized once");

        session = Setup(out root, out first, out second);
        session.Parts.BaseWork = null;
        a = new Probe(); b = new Probe();
        bool failed = false;
        try { AssemblyTraverser.Run(Config(output), new ITransactionHandler[] { a, b }); }
        catch (InvalidOperationException) { failed = true; }
        Check(failed && a.Finishes == 1 && b.Finishes == 1, "Preflight failure still finalizes accepted handlers");

        session = Setup(out root, out first, out second);
        a = new Probe(); failed = false;
        try { AssemblyTraverser.Run(Config(output), new ITransactionHandler[] { a, a }); }
        catch (ArgumentException) { failed = true; }
        Check(failed && a.Calls == 0 && a.Finishes == 0, "Reject duplicate instance before accepting ownership");

        session = Setup(out root, out first, out second);
        var changedPart = (Part)first.Prototype;
        a = new Probe { Work = c => c.Session.Parts.BaseWork = c.TargetPart };
        b = new Probe();
        AssemblyTraverser.Run(Config(output), new ITransactionHandler[] { a, b });
        Check(AssemblyTraverser.StopRequested && b.Calls == 0, "Context violation stops subsequent plugins");
        Check(!changedPart.Closed && a.Finishes == 1 && b.Finishes == 1,
            "Failed context verification preserves parts and still finalizes handlers");

        failed = false;
        try { new ImageExporter(new ImageExporter.SnapViewType[0]); } catch (ArgumentException) { failed = true; }
        Check(failed, "Explicit registration cannot bypass image option validation");
        failed = false;
        try { new ImageExporter(new[] { (ImageExporter.SnapViewType)999 }); } catch (ArgumentException) { failed = true; }
        Check(failed, "Invalid image view rejected at composition time");
    }
    private static void SharedServicesAndIsolation(string output)
    {
        foreach (bool imageFails in new[] { false, true })
        {
            Part root; Component first, second;
            Session session = Setup(out root, out first, out second);
            var config = Config(output); config.idPatterns = new[] { "first" };
            View view = root.Views.WorkView;
            var auxiliary = new Point { Tag=(Tag)4500, OwningComponent=first };
            view.Scene.Add(auxiliary);
            UFSession.Instance.Assem.Members.Clear();
            UFSession.Instance.Assem.Members[first.Tag] = new NXObject[] { view.Scene[0] };
            UFSession.Instance.Modl.Fail = UFSession.Instance.Obj.Fail = false;
            UFSession.Instance.Modl.AfterQuery = null;
            UFSession.Instance.Modl.Vertices[(Tag)3000] = new[] { new double[]{0,0,0}, new double[]{2,3,4} };
            int captures = 0;
            session.CaptureObserver = v => {
                ++captures;
                Check(auxiliary.IsBlanked && !v.WcsVisibility, "Frame service hides auxiliaries and axes");
                if (imageFails) throw new IOException("simulated image failure");
            };
            var after = new Probe { Work = c => {
                Check(!auxiliary.IsBlanked && view.WcsVisibility && view.Matrix.Xx == 1,
                    "Next plugin sees restored frame even after image failure");
                Check(second.IsBlanked, "Target mask remains shared between handlers");
                BoundingBoxResult box = c.MeasureBoundingBox("ACS");
                Check(box.X == 2 && box.Y == 3 && box.Z == 4, "New plugin reuses exact measurement service");
            }};
            // Reversed from defaults: image then another plugin performing XYZ.
            AssemblyTraverser.Run(config, new ITransactionHandler[] { new ImageExporter(new[]{ImageExporter.SnapViewType.Top, ImageExporter.SnapViewType.Top}), after });
            Check(captures == 1, "Duplicate image views are captured only once");
            Check(after.Calls == 1 && after.Finishes == 1, "Custom handler works after image, irrespective of success");
            Check(session.ListingWindow.Lines.FindAll(x=>x.Contains("[Export context]")).Count == 1,
                "Only one target display scope for all handlers");
            Check(session.Calls.FindAll(x=>x=="partial readonly prototype").Count == 0,
                "Traversal and display preparation both skip redundant partial loads");
            Check(!second.IsBlanked && !auxiliary.IsBlanked, "Target mask restored after all handlers");
        }
    }
}
