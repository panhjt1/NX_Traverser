// Runs the real traverser, NX-call wrapper and cancellation monitor with NX/UI
// doubles. Windows Forms and NX's native cancellation processing need local tests.
using System;
using System.IO;
using System.Threading;
using NXOpen;
using NXOpen.Assemblies;
using NXOpen.UF;
using PartLoadState = NXOpen.PartLoadState;

internal static class TraversalCancellationTests
{
    private static int _checks;
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception("Cancellation regression: " + message); ++_checks; }
    private static void Canceled(Action action, string message)
    {
        bool canceled = false;
        try { action(); } catch (OperationCanceledException) { canceled = true; }
        Check(canceled, message);
    }
    private static void Reset()
    {
        AssemblyTraverser.StopRequested = false;
        UFSession.Instance.Abort.Flag = false;
        UFSession.Instance.Abort.Fail = false;
        UFSession.Instance.Abort.Clears = 0;
        TraversalStopWindow.OnOpen = null;
        TraversalStopWindow.Opens = TraversalStopWindow.Closes = 0;
    }

    public static int Run()
    {
        NativeSignalAndCleanup();
        FullTraversal();
        Reset();
        return _checks;
    }

    private static void NativeSignalAndCleanup()
    {
        Reset();
        using (var control = new TraversalCancellation())
        {
            bool ran = false;
            UFSession.Instance.Abort.Flag = true;
            Canceled(() => TraversalCancellation.NxCall("before", () => ran = true), "Pending native cancel prevents next operation");
            Check(!ran && AssemblyTraverser.StopRequested && !UFSession.Instance.Abort.Flag && UFSession.Instance.Abort.Clears == 1,
                "Latch stop before clearing NX's transient signal");
            Canceled(TraversalCancellation.Checkpoint, "Stop stays latched after native flag is cleared");
            TraversalCancellation.NxCall("cleanup", () => ran = true, cancelable: false);
            Check(ran, "Cleanup is not skipped by persistent stop flag");
        }
        Reset();
        using (var control = new TraversalCancellation())
        {
            Canceled(() => TraversalCancellation.NxCall("returns normally", () => UFSession.Instance.Abort.Flag = true),
                "Native cancel returned without exception still stops the task");
        }
        Reset();
        using (var control = new TraversalCancellation())
        {
            Canceled(() => TraversalCancellation.NxCall("throws", () => { UFSession.Instance.Abort.Flag = true; throw new NXException(123); }),
                "Pending cancel accompanying NX exception is not just a failed node");
        }
        Reset();
        using (var control = new TraversalCancellation())
        {
            Canceled(() => TraversalCancellation.NxCall("managed cancellation", () => { throw new OperationCanceledException(); }),
                "Cancellation exception sets global latch even without native flag");
            Check(AssemblyTraverser.StopRequested, "Managed cancellation persists");
        }
        Reset();
        using (var control = new TraversalCancellation())
        {
            bool sawError = false;
            try { TraversalCancellation.NxCall("ordinary error", () => { throw new NXException(641070); }); }
            catch (NXException) { sawError = true; }
            Check(sawError && !AssemblyTraverser.StopRequested, "Ordinary load error does not become user cancel");
        }
        Reset();
        using (var control = new TraversalCancellation())
        {
            UFSession.Instance.Abort.Fail = true;
            bool failed = false;
            try { TraversalCancellation.Checkpoint(); } catch (InvalidOperationException) { failed = true; }
            Check(failed && AssemblyTraverser.StopRequested, "Cancellation monitor failure stops further work");
            UFSession.Instance.Abort.Fail = false;
        }
        Reset();
        using (var control = new TraversalCancellation())
        {
            Canceled(() => TraversalCancellation.NxCall("blocked NX equivalent", () => {
                // A separate UI thread may request stop without touching NX.
                var thread = new Thread(AssemblyTraverser.RequestStop);
                thread.Start(); thread.Join();
            }), "Managed UI stop survives a native call that consumes/does not set abort");
        }
    }

    private static Session Setup(out Part root, out Component first, out Component second)
    {
        Reset();
        var session = ImageViewTests.Setup(out root, out first);
        first.DisplayName = "first";
        second = root.ComponentAssembly.RootComponent.Children[1];
        second.DisplayName = "second";
        second.Prototype = session.Add("second prototype", PartLoadState.PartiallyLoaded);
        UFSession.Instance.Modl.Fail = false;
        UFSession.Instance.Modl.AfterQuery = null;
        UFSession.Instance.Obj.Fail = false;
        UFSession.Instance.Obj.Deleted.Clear();
        UFSession.Instance.Obj.States.Clear();
        UFSession.Instance.Obj.States[(Tag)9002] = UFConstants.UF_OBJ_CONDEMNED;
        UFSession.Instance.Assem.Members.Clear();
        UFSession.Instance.Assem.Members[first.Tag] = new NXObject[] { root.Views.WorkView.Scene[0] };
        UFSession.Instance.Modl.Vertices[(Tag)3000] = new[] { new double[]{0,0,0}, new double[]{2,3,4} };
        return session;
    }

    private static TraversalConfig Config(string output, bool measure = false)
    {
        return new TraversalConfig { filePath = output, flagImageOn = true, flagXYZOn = measure,
            viewList = new[] { ImageExporter.SnapViewType.Top, ImageExporter.SnapViewType.Front } };
    }
    private static void FullTraversal()
    {
        string output = Path.Combine(Path.GetTempPath(), "nx-cancel-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            Part root; Component first, second;
            Session session = Setup(out root, out first, out second);
            Part firstPart = (Part)first.Prototype, secondPart = (Part)second.Prototype;
            int images = 0;
            session.CaptureObserver = view => { ++images; AssemblyTraverser.RequestStop(); };
            AssemblyTraverser.Run(Config(output));
            Check(images == 1, "Stop during capture skips remaining views and sibling components");
            Check(firstPart.Closed && !secondPart.Closed && !root.Closed, "Cancel releases touched target and preserves untouched sibling/root");
            Check(session.Parts.Work == root && session.Parts.Display == root && !second.IsBlanked,
                "Canceled traversal restores original assembly display and mask");
            Check(root.Views.WorkView.Scale == 7 && root.Views.WorkView.WcsVisibility,
                "Canceled traversal restores camera and overlays");
            Check(!session.Calls.Contains("partial second prototype"), "No next sibling load after stop");
            Check(TraversalStopWindow.Opens == 1 && TraversalStopWindow.Closes == 1,
                "Control window lifetime surrounds traversal and cleanup");
            Check(session.ListingWindow.Lines.Exists(x => x.Contains("失败=0") && x.Contains("停止=True")),
                "User cancel is not counted as per-part failure");

            session = Setup(out root, out first, out second);
            firstPart = (Part)first.Prototype;
            UFSession.Instance.Modl.AfterQuery = () => UFSession.Instance.Abort.Flag = true;
            AssemblyTraverser.Run(Config(output, true));
            Check(session.LastImageBuilder == null && firstPart.Closed, "Native measurement cancel skips image handler and cleans target");
            Check(TransactionContractTests.ReadRunLog(session).Contains("[CANCELED] BoundingBoxExporter"),
                "Native cancel has explicit task cancellation evidence in the file log");
            string logOutput = session.ListingWindow.Lines.Find(x => x.Contains("输出目录: "));
            string directory = logOutput.Substring(logOutput.IndexOf("输出目录: ", StringComparison.Ordinal) + "输出目录: ".Length);
            Check(!File.Exists(Path.Combine(directory, "BoundingBoxResult.csv")), "No dimension row after canceled exact query");
            Check(UFSession.Instance.Obj.Deleted.Count == 1 && UFSession.Instance.Obj.Deleted[0] == (Tag)9003,
                "Canceled exact query cleans only the owned CSYS");

            session = Setup(out root, out first, out second);
            firstPart = (Part)first.Prototype;
            images = 0;
            session.CaptureObserver = view => ++images;
            var cleanupConfig = Config(output, true);
            cleanupConfig.idPatterns = new[] { "first" };
            AssemblyTraverser.Run(cleanupConfig);
            Check(images == 2 && firstPart.Closed && !AssemblyTraverser.StopRequested,
                "Condemned orientation support does not prevent following image transaction and per-node unload");
            Check(UFSession.Instance.Obj.Deleted.Count==1 && UFSession.Instance.Obj.Deleted[0]==(Tag)9003 &&
                UFSession.Instance.Obj.States[(Tag)9002]==UFConstants.UF_OBJ_CONDEMNED,
                "Full traversal leaves the NX-managed matrix alone");
            Check(session.ListingWindow.Lines.Exists(x => x.Contains("成功事务=2") && x.Contains("失败=0") && x.Contains("停止=False")),
                "Verified cleanup is counted as a successful measurement, not a retained-parts stop");

            session = Setup(out root, out first, out second);
            images = 0;
            session.CaptureObserver = view => { ++images; if (images == 1) throw new IOException("ordinary export error"); };
            AssemblyTraverser.Run(Config(output));
            Check(images == 3 && !AssemblyTraverser.StopRequested,
                "Ordinary export failure continues to next target, unlike cancellation");

            // Next run clears only the managed previous-run latch; a new request
            // arriving during window startup is honored before loading any node.
            session = Setup(out root, out first, out second);
            AssemblyTraverser.StopRequested = true;
            TraversalStopWindow.OnOpen = () => {
                Check(!AssemblyTraverser.StopRequested, "New run resets the previous run's managed latch");
                AssemblyTraverser.RequestStop();
            };
            AssemblyTraverser.Run(Config(output));
            Check(session.Calls.Count == 0 && session.LastImageBuilder == null, "Stop at startup prevents all target loading");
            Check(TraversalStopWindow.Closes == 1, "Startup cancellation closes control window");
        }
        finally { Directory.Delete(output, true); }
    }
}

// No Windows Forms code runs in cross-platform tests. Its real source is also
// compiled separately against .NET Framework references (not run on Linux).
internal sealed class TraversalStopWindow : IDisposable
{
    public static bool HasLiveWindow { get { return false; } }
    public static Action OnOpen;
    public static int Opens, Closes;
    public TraversalStopWindow() { ++Opens; if (OnOpen != null) OnOpen(); }
    public void Dispose() { ++Closes; }
}
namespace NXOpen.UF
{
    public sealed class UFAbort
    {
        public bool Flag, Fail;
        public int Clears;
        public void AskFlagStatus(out bool value)
        { if (Fail) throw new InvalidOperationException("simulated abort API error"); value = Flag; }
        public void ClearAbort() { ++Clears; Flag = false; }
    }
    public sealed class UFUi { public void SetStatus(string status) { } }
}
