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
        Check(!AssemblyTraverser.StopRequested, "Successful cleanup does not request stop");

        using (var resources = new NxPartResources(session))
        {
            NxPartResources.BeforeLoad(null);
            Part refused = session.Add("refused close", PartLoadState.FullyLoaded);
            refused.RefuseClose = true;
            Check(!resources.ReleaseUnused("refused"), "Detect close that returns without unloading");
            Check(AssemblyTraverser.StopRequested && !refused.Closed, "Stop further loading when release fails");
            refused.RefuseClose = false;
        }
        AssemblyTraverser.StopRequested = false;
        using (var resources = new NxPartResources(session))
        {
            NxPartResources.BeforeLoad(null);
            Part throwing = session.Add("throwing close", PartLoadState.PartiallyLoaded);
            throwing.ThrowOnClose = true;
            Check(!resources.ReleaseUnused("exception") && AssemblyTraverser.StopRequested, "Close exception requests stop");
            throwing.ThrowOnClose = false;
        }
        CheckLoadStatus();
        CheckInvalidationDuringClose();
        return _checks;
    }

    private static void CheckInvalidationDuringClose()
    {
        AssemblyTraverser.StopRequested = false;
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
            Check(parent.Closed && child.Closed && !AssemblyTraverser.StopRequested, "Cascade release completes without false stop");

            NxPartResources.BeforeLoad(null);
            Part ended = session.Add("inactive after close", PartLoadState.PartiallyLoaded);
            ended.InactiveAfterClose = true;
            Check(resources.ReleaseUnused("inactive but gone"), "Ignore 3615013 only after confirming disappearance");

            NxPartResources.BeforeLoad(null);
            Part live = session.Add("inactive but still loaded", PartLoadState.PartiallyLoaded);
            live.InactiveBeforeClose = true;
            Check(!resources.ReleaseUnused("inactive but live") && AssemblyTraverser.StopRequested, "Do not swallow 3615013 while part remains loaded");
            live.InactiveBeforeClose = false;
        }
        Check(session.Parts.Items.Count == 1 && !root.Closed, "Final sweep leaves protected root intact");
    }

    private static void CheckLoadStatus()
    {
        NxGeometry.CheckLoadStatus(new PartLoadStatus { Codes = new[] { 641058 } }, "warning");
        bool failed = false;
        try { NxGeometry.CheckLoadStatus(new PartLoadStatus { Codes = new[] { 641058, 641070 } }, "mixed"); }
        catch (InvalidOperationException) { failed = true; }
        Check(failed, "Read-only warning must not hide another load error");
    }

}

namespace NXOpen
{
    public sealed class ListingWindow
    {
        public readonly List<string> Lines = new List<string>();
        public void Open() { }
        public void WriteLine(string line) { Lines.Add(line); }
    }
    public class NXException : Exception
    {
        public int ErrorCode { get; private set; }
        public NXException(int code) : base("Simulated NX error") { ErrorCode = code; }
    }
    public enum Tag { Null }
    public enum PartLoadState { NotLoaded, FullyLoaded, PartiallyLoaded, MinimallyLoaded }
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
        public bool Closed, RefuseClose, ThrowOnClose;
        public bool ThrowOnPartialLoad;
        public PartLoadStatus LoadThisPartPartially()
        {
            Owner.Calls.Add("partial " + Name);
            if (ThrowOnPartialLoad) throw new InvalidOperationException("Simulated partial load failure");
            if (!IsFullyLoaded) PartLoadState = PartLoadState.PartiallyLoaded;
            return new PartLoadStatus();
        }
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
        public enum Units { Millimeters, Inches }
        public Units PartUnits = Units.Millimeters;
        public readonly WCS WCS = new WCS();
        public readonly Layer.LayerManager Layers = new Layer.LayerManager();
        public readonly List<Facet.FacetedBody> FacetedBodies = new List<Facet.FacetedBody>();
        public NXObject.AttributeInformation GetUserAttribute(string title, NXObject.AttributeType type, int index)
        { return new NXObject.AttributeInformation(); }
        public string FullPath { get { return Name; } }
        public ViewCollection Views;
        public ViewCollection ModelingViews { get { return Views; } }
        public Assemblies.ComponentAssembly ComponentAssembly = new Assemblies.ComponentAssembly();
        public readonly List<Body> Bodies = new List<Body>();
    }
    public sealed class PartCollection
    {
        public readonly List<BasePart> Items = new List<BasePart>();
        public readonly List<BasePart> Displayed = new List<BasePart>();
        public BasePart BaseWork;
        public Assemblies.Component WorkComponent;
        public Part Work { get { return BaseWork as Part; } }
        public Part Display { get { return Displayed.Count == 0 ? null : Displayed[0] as Part; } }
        public BasePart[] ToArray() { return Items.ToArray(); }
        public BasePart[] GetDisplayedParts() { return Displayed.ToArray(); }
    }
    public sealed class Session
    {
        public static Session GetSession() { return UI.SessionOwner; }
        public readonly ListingWindow ListingWindow = new ListingWindow();
        private int _next;
        public readonly PartCollection Parts = new PartCollection();
        public readonly List<string> Calls = new List<string>();
        public readonly DisplayManager DisplayManager = new DisplayManager();
        public Action<View> CaptureObserver;
        public Gateway.ImageExportBuilder LastImageBuilder;
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
    public sealed class Component : DisplayableObject, IFitTo
    {
        public Component Parent;
        public object Prototype;
        public string DisplayName = "test occurrence";
        public bool IsSuppressed;
        public readonly List<Component> Children = new List<Component>();
        public Component[] GetChildren() { return Children.ToArray(); }
        public NXObject FindOccurrence(NXObject proto) { throw new NotSupportedException("Not simulated by these tests"); }
    }
    public sealed class ComponentAssembly
    {
        public enum OpenOption { ComponentOnly }
        public enum OpenComponentStatus { SuccessfullyOpened }
        public Component RootComponent;
        public PartLoadStatus OpenComponents(OpenOption option, Component[] components, out OpenComponentStatus[] status)
        {
            status = null;
            throw new InvalidOperationException("Unexpected attempt to open an unloaded off-target component");
        }
    }
}
