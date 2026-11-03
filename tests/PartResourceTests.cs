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
        return _checks;
    }
}

namespace NXOpen
{
    public enum Tag { Null }
    public enum PartLoadState { NotLoaded, FullyLoaded, PartiallyLoaded, MinimallyLoaded }
    public class BasePart
    {
        public enum CloseWholeTree { False, True }
        public enum CloseModified { CloseModified }
        internal Session Owner;
        public Tag Tag;
        public string Name;
        public bool IsModified;
        public bool IsReadOnly = true;
        public PartLoadState PartLoadState;
        public bool IsFullyLoaded { get { return PartLoadState == PartLoadState.FullyLoaded; } }
        public bool Closed, RefuseClose, ThrowOnClose;
        public void Close(CloseWholeTree tree, CloseModified modified, object responses)
        {
            if (tree != CloseWholeTree.False || modified != CloseModified.CloseModified || responses != null)
                throw new Exception("Unexpected close options");
            if (ThrowOnClose) throw new Exception("Simulated NX close error");
            if (RefuseClose) return;
            Closed = true;
            Owner.Parts.Items.Remove(this);
        }
    }
    public sealed class Part : BasePart { }
    public sealed class PartCollection
    {
        public readonly List<BasePart> Items = new List<BasePart>();
        public readonly List<BasePart> Displayed = new List<BasePart>();
        public BasePart BaseWork;
        public Assemblies.Component WorkComponent;
        public BasePart[] ToArray() { return Items.ToArray(); }
        public BasePart[] GetDisplayedParts() { return Displayed.ToArray(); }
    }
    public sealed class Session
    {
        private int _next;
        public readonly PartCollection Parts = new PartCollection();
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
    }
}
internal static class AssemblyTraverser
{
    public static bool Stopped;
    public static void RequestStop() { Stopped = true; }
}
internal static class RunLog
{
    public static void Write(string text) { }
    public static void Error(string stage, Exception error) { }
}
