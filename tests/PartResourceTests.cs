// Test doubles for ownership/cleanup control flow ONLY. These are NOT NX SDK
// references and cannot validate NX signatures, unload behavior, or native UI.
using System;
using System.Collections.Generic;
using NXOpen;

internal static class PartResourceTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Resource regression: " + message);
        ++_checks;
    }

    public static int Run()
    {
        var session = new Session();
        Part root = session.Add("root", PartLoadState.FullyLoaded);
        session.Parts.BaseWork = root;
        session.Parts.Displayed.Add(root);
        Part full = session.Add("preexisting full", PartLoadState.FullyLoaded);
        Part dirty = session.Add("preexisting dirty", PartLoadState.PartiallyLoaded, true);
        Part partial = session.Add("promoted", PartLoadState.MinimallyLoaded);
        Part implicitPromotion = session.Add("implicit promotion", PartLoadState.MinimallyLoaded);
        Part untouched = session.Add("untouched", PartLoadState.PartiallyLoaded);
        Part otherWindow = session.Add("other display", PartLoadState.PartiallyLoaded);
        session.Parts.Displayed.Add(otherWindow);
        Part workAncestor = session.Add("work ancestor", PartLoadState.PartiallyLoaded);
        session.Parts.WorkComponent = new NXOpen.Assemblies.Component { OwningPart = workAncestor, Prototype = root };

        using (var resources = new NxPartResources(session))
        {
            NxPartResources.BeforeLoad(partial);
            partial.PartLoadState = PartLoadState.FullyLoaded;
            partial.IsModified = true; // NX load upgrade, not a preexisting edit.
            implicitPromotion.PartLoadState = PartLoadState.PartiallyLoaded;
            NxPartResources.BeforeLoad(full);
            NxPartResources.BeforeLoad(dirty);
            NxPartResources.BeforeLoad(otherWindow);
            NxPartResources.BeforeLoad(workAncestor);
            NxPartResources.BeforeLoad(null);
            Part parent = session.Add("new active ancestor", PartLoadState.PartiallyLoaded);
            resources.Pin(parent);
            Part dependency = session.Add("dependency opened before error", PartLoadState.FullyLoaded, true);
            Check(resources.ReleaseUnused("failed child load"), "Cleanup after a failed load succeeds");
            Check(partial.Closed && implicitPromotion.Closed, "Both explicit and implicit promotions are released");
            Check(dependency.Closed, "Failed load dependencies are released even without an explicit touch");
            Check(!parent.Closed, "An active ancestor survives child cleanup");
            Check(!full.Closed && !dirty.Closed, "Preexisting geometry and edits survive");
            Check(!untouched.Closed, "Unvisited partial data survives");
            Check(!otherWindow.Closed && !workAncestor.Closed && !root.Closed, "Windows and work ancestors survive");
            resources.Unpin(parent.Tag);
            Check(resources.ReleaseUnused("parent finally") && parent.Closed, "Deferred parent is released after child sweep");

            NxPartResources.BeforeLoad(null);
            Part active = session.Add("temporary active work", PartLoadState.FullyLoaded);
            session.Parts.BaseWork = active;
            resources.ReleaseUnused("restore failed");
            Check(!active.Closed, "Never close the current work part on failed restoration");
            session.Parts.BaseWork = root;
            NxPartResources.BeforeLoad(null);
            resources.ReleaseUnused("restored");
            Check(active.Closed, "Release temporary work part after restoration");
        }
        Check(session.Parts.Items.Count == 6, "Only protected and untouched initial parts remain");
        Check(!AssemblyTraverser.Stopped, "Successful cleanup does not request stop");

        using (var resources = new NxPartResources(session))
        {
            NxPartResources.BeforeLoad(null);
            Part refused = session.Add("refused close", PartLoadState.FullyLoaded);
            refused.RefuseClose = true;
            Check(!resources.ReleaseUnused("refused"), "Detect close that returns without unloading");
            Check(AssemblyTraverser.Stopped && !refused.Closed, "Stop further loading when release fails");
            refused.RefuseClose = false;
        }
        AssemblyTraverser.Stopped = false;
        using (var resources = new NxPartResources(session))
        {
            NxPartResources.BeforeLoad(null);
            Part throwing = session.Add("throwing close", PartLoadState.PartiallyLoaded);
            throwing.ThrowOnClose = true;
            Check(!resources.ReleaseUnused("exception") && AssemblyTraverser.Stopped, "Close exception requests stop");
            throwing.ThrowOnClose = false;
        }
        CheckDisplayContext();
        CheckInvalidationDuringClose();
        return _checks;
    }

    private static void CheckInvalidationDuringClose()
    {
        AssemblyTraverser.Stopped = false;
        var session = new Session();
        Part root = session.Add("root", PartLoadState.FullyLoaded);
        session.Parts.BaseWork = root;
        session.Parts.Displayed.Add(root);
        using (var resources = new NxPartResources(session))
        {
            NxPartResources.BeforeLoad(null);
            Part child = session.Add("dependency", PartLoadState.PartiallyLoaded);
            Part parent = session.Add("assembly", PartLoadState.FullyLoaded);
            parent.ReleaseWithClose = child; // Parent is visited first in this enumeration.
            Check(resources.ReleaseUnused("cascade"), "Implicit dependent release must not read an inactive wrapper");
            Check(parent.Closed && child.Closed && !AssemblyTraverser.Stopped, "Cascade release completes without false stop");

            NxPartResources.BeforeLoad(null);
            Part ended = session.Add("inactive after close", PartLoadState.PartiallyLoaded);
            ended.InactiveAfterClose = true;
            Check(resources.ReleaseUnused("inactive but gone"), "Ignore 3615013 only after confirming disappearance");

            NxPartResources.BeforeLoad(null);
            Part live = session.Add("inactive but still loaded", PartLoadState.PartiallyLoaded);
            live.InactiveBeforeClose = true;
            Check(!resources.ReleaseUnused("inactive but live") && AssemblyTraverser.Stopped, "Do not swallow 3615013 while part remains loaded");
            live.InactiveBeforeClose = false;
        }
        Check(session.Parts.Items.Count == 1 && !root.Closed, "Final sweep leaves protected root intact");
    }

    private static void CheckDisplayContext()
    {
        var session = new Session();
        Part original = session.Add("original display", PartLoadState.FullyLoaded);
        session.Parts.BaseWork = original;
        session.Parts.Displayed.Add(original);
        Part target = session.Add("read-only target", PartLoadState.PartiallyLoaded);
        session.Parts.LoadCodes = new[] { 641058 };
        using (var context = new NxPartContext(session, target))
        {
            Check(session.Calls.Count == 2 && session.Calls[0] == "load read-only target" &&
                session.Calls[1] == "display read-only target", "Target is loaded before activation");
            Check(target.IsFullyLoaded && session.Parts.Work == target, "Read-only warning permits a loaded target");
        }
        Check(session.Parts.Work == original && session.Parts.Display == original, "Restore original context");
        Check(session.Calls.Count == 3 && session.Calls[2] == "display original display", "Do not preload original display on restoration");

        bool failed = false;
        try { NxPartContext.CheckLoadStatus(new PartLoadStatus { Codes = new[] { 641058, 641070 } }, "mixed"); }
        catch (InvalidOperationException) { failed = true; }
        Check(failed, "Read-only warning must not hide another load error");

        session.Calls.Clear();
        target.ThrowOnLoad = true;
        target.PartLoadState = PartLoadState.PartiallyLoaded;
        failed = false;
        try { using (var context = new NxPartContext(session, target)) { } }
        catch (InvalidOperationException) { failed = true; }
        Check(failed && session.Calls.Count == 1, "Failed preload never enters SetActiveDisplay");
        Check(session.Parts.Work == original && session.Parts.Display == original, "Failed preload leaves original context intact");
    }
}

namespace NXOpen
{
    public class NXException : Exception
    {
        public int ErrorCode { get; private set; }
        public NXException(int code) : base("Simulated NX error") { ErrorCode = code; }
    }
    public enum Tag { Null }
    public enum PartLoadState { NotLoaded, FullyLoaded, PartiallyLoaded, MinimallyLoaded }
    public enum DisplayPartOption { ReplaceExisting }
    public enum PartDisplayPartWorkPartOption { SameAsDisplay }
    public sealed class PartLoadStatus : IDisposable
    {
        public int[] Codes = new int[0];
        public int NumberUnloadedParts { get { return Codes.Length; } }
        public int GetStatus(int index) { return Codes[index]; }
        public string GetPartName(int index) { return "test part"; }
        public string GetStatusDescription(int index) { return "test status"; }
        public void Dispose() { }
    }
    public class BasePart
    {
        public enum CloseWholeTree { False, True }
        public enum CloseModified { CloseModified }
        internal Session Owner;
        public Tag Tag;
        private string _name;
        private bool _modified;
        private PartLoadState _state;
        private void CheckAlive() { if (Closed) throw new NXException(3615013); }
        public string Name { get { CheckAlive(); return _name; } set { _name = value; } }
        public bool IsModified { get { CheckAlive(); return _modified; } set { _modified = value; } }
        public bool IsReadOnly = true;
        public PartLoadState PartLoadState { get { CheckAlive(); return _state; } set { _state = value; } }
        public bool IsFullyLoaded { get { return PartLoadState == PartLoadState.FullyLoaded; } }
        public bool Closed, RefuseClose, ThrowOnClose, ThrowOnLoad;
        public bool InactiveBeforeClose, InactiveAfterClose;
        public BasePart ReleaseWithClose;
        public void Close(CloseWholeTree tree, CloseModified modified, object responses)
        {
            if (tree != CloseWholeTree.False || modified != CloseModified.CloseModified || responses != null)
                throw new Exception("Unexpected close options");
            if (ThrowOnClose) throw new Exception("Simulated NX close error");
            if (InactiveBeforeClose) throw new NXException(3615013);
            if (RefuseClose) return;
            Closed = true;
            Owner.Parts.Items.Remove(this);
            if (ReleaseWithClose != null)
            {
                ReleaseWithClose.Closed = true;
                Owner.Parts.Items.Remove(ReleaseWithClose);
            }
            if (InactiveAfterClose) throw new NXException(3615013);
        }
    }
    public sealed class Part : BasePart
    {
        public string FullPath { get { return Name; } }
        public ViewCollection Views;
        public ViewCollection ModelingViews { get { return Views; } }
        public LayoutCollection Layouts;
        public Assemblies.ComponentAssembly ComponentAssembly;
    }
    public sealed class PartCollection
    {
        public enum SdpsStatus { Ok }
        public enum RefsetOption { PreserveRefset }
        public enum WorkComponentOption { Given }
        public readonly List<BasePart> Items = new List<BasePart>();
        public readonly List<BasePart> Displayed = new List<BasePart>();
        public BasePart BaseWork;
        public Assemblies.Component WorkComponent;
        public Part Work { get { return BaseWork as Part; } }
        public Part Display { get { return Displayed.Count == 0 ? null : Displayed[0] as Part; } }
        public int[] LoadCodes = new int[0];
        public SdpsStatus SetActiveDisplay(Part part, DisplayPartOption displayOption,
            PartDisplayPartWorkPartOption workOption, out PartLoadStatus status)
        {
            part.Owner.Calls.Add("display " + part.Name);
            Displayed[0] = part;
            BaseWork = part;
            WorkComponent = null;
            status = new PartLoadStatus { Codes = LoadCodes };
            return SdpsStatus.Ok;
        }
        public void SetWorkComponent(Assemblies.Component component, RefsetOption refset,
            WorkComponentOption option, out PartLoadStatus status)
        {
            WorkComponent = component;
            BaseWork = component.Prototype as Part;
            status = new PartLoadStatus();
        }
        public void SetWork(Part part) { BaseWork = part; }
        public BasePart[] ToArray() { return Items.ToArray(); }
        public BasePart[] GetDisplayedParts() { return Displayed.ToArray(); }
    }
    public sealed class Session
    {
        private int _next;
        public readonly PartCollection Parts = new PartCollection();
        public readonly List<string> Calls = new List<string>();
        public readonly DisplayManager DisplayManager = new DisplayManager();
        public Action<View> CaptureObserver;
        public Part Add(string name, PartLoadState state, bool modified = false)
        {
            var part = new Part { Owner = this, Tag = (Tag)(++_next), Name = name, PartLoadState = state, IsModified = modified };
            Parts.Items.Add(part);
            return part;
        }
    }
}
namespace NXOpen.Assemblies
{
    public sealed class Component
    {
        public Component Parent;
        public Part OwningPart;
        public object Prototype;
        public Tag Tag;
        public string DisplayName = "test occurrence";
    }
}
internal static class AssemblyTraverser
{
    public static bool Stopped;
    public static bool StopRequested { get { return Stopped; } }
    public static void RequestStop() { Stopped = true; }
}
internal static class RunLog
{
    public static void Write(string text) { }
    public static void Error(string stage, Exception error) { }
    public static void NxCall(string stage, Action action) { action(); }
}
// Simulate the outcome of the load adapter; actual geometry loading is NOT
// validated by this double. NxPartContext itself is compiled from production.
internal static class NxGeometry
{
    public static void PrepareDisplay(Part part) { }
    public static void LoadFully(Part part)
    {
        part.Owner.Calls.Add("load " + part.Name);
        if (part.ThrowOnLoad) throw new InvalidOperationException("Simulated load failure");
        part.PartLoadState = PartLoadState.FullyLoaded;
    }
}
