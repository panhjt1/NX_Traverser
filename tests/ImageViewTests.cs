// Control-flow doubles only. These do not render PNGs, reproduce NX dialogs,
// or prove compatibility with Siemens binaries.
using System;
using System.IO;
using NXOpen;
using NXOpen.Assemblies;

internal static class ImageViewTests
{
    private static int _checks;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("Image regression: " + message);
        ++_checks;
    }
    private static void Fails<T>(Action action, string message) where T : Exception
    {
        bool failed = false;
        try { action(); } catch (T) { failed = true; }
        Check(failed, message);
    }
    internal static Session Setup(out Part root, out Component component)
    {
        AssemblyTraverser.Stopped = false;
        var session = new Session();
        root = session.Add("display root", PartLoadState.FullyLoaded);
        session.Parts.BaseWork = root;
        session.Parts.Displayed.Add(root);
        root.Views = new ViewCollection { WorkView = NewView(1000) };
        Part target = session.Add("readonly prototype", PartLoadState.PartiallyLoaded);
        target.Views = new ViewCollection { WorkView = NewView(1001) };
        Component rootComponent = new Component { Tag = (Tag)1999, OwningPart = root, Prototype = root };
        root.ComponentAssembly.RootComponent = rootComponent;
        component = new Component { Tag = (Tag)2000, OwningPart = root, Parent = rootComponent, Prototype = target };
        rootComponent.Children.Add(component);
        // An unloaded sibling must be hidden without opening its prototype.
        rootComponent.Children.Add(new Component { Tag = (Tag)2001, OwningPart = root, Parent = rootComponent });
        root.Views.WorkView.Scene.Add(new Body { Tag = (Tag)3000, OwningPart = target, OwningComponent = component });
        root.Views.WorkView.Scene.Add(new Body { Tag = (Tag)3001, OwningPart = root, OffscreenUntilFit = true });
        root.Views.WorkView.Scene.Add(new Body { Tag = (Tag)3002, OwningPart = root,
            OwningComponent = rootComponent.Children[1] });
        NXOpen.UF.UFSession.Instance.Assem.Explosion = Tag.Null;
        NXOpen.UF.UFSession.Instance.Assem.Transform = IdentityTransform();
        UI.SessionOwner = session;
        return session;
    }
    private static double[,] IdentityTransform()
    { return new double[,] { { 1, 0, 0, 0 }, { 0, 1, 0, 0 }, { 0, 0, 1, 0 }, { 0, 0, 0, 1 } }; }
    private static View NewView(int tag)
    {
        return new ModelingView { Tag = (Tag)tag, Scale = 7, WcsVisibility = true, TriadVisibility = false,
            Matrix = new Matrix3x3 { Xx = 1, Yy = 1, Zz = 1 } };
    }
    private static void Capture(Session session, Component component, string output, string name,
        params ImageExporter.SnapViewType[] views)
    {
        new ImageExporter(views).Process(component, session, output, name);
    }
    public static int Run()
    {
        string output = Path.Combine(Path.GetTempPath(), "nx-image-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            var config = new TraversalConfig { filePath = output, flagImageOn = true,
                viewList = new[] { ImageExporter.SnapViewType.Top } };
            config.Validate();
            Check(!config.useIsolatedImageView, "Isolation remains disabled");
            config.useIsolatedImageView = true;
            Fails<NotSupportedException>(config.Validate, "Old isolation configuration is rejected before traversal");
            Fails<NotSupportedException>(() => new ImageExporter(config.viewList, true), "Direct callers cannot enable isolation");

            foreach (bool wcs in new[] { false, true })
                foreach (bool triad in new[] { false, true })
                {
                    Part root; Component component;
                    Session session = Setup(out root, out component);
                    Part target = (Part)component.Prototype;
                    View view = root.Views.WorkView;
                    view.WcsVisibility = wcs; view.TriadVisibility = triad;
                    session.CaptureObserver = active =>
                    {
                        Check(active == view && !active.WcsVisibility && !active.TriadVisibility,
                            "Target view has both overlays hidden during capture");
                        Check(session.LastImageBuilder.EnhanceEdges && !session.LastImageBuilder.RegionMode,
                            "Ordinary full-view capture retains edge enhancement");
                    };
                    Capture(session, component, output, "image" + wcs + triad, ImageExporter.SnapViewType.Trimetric);
                    Check(view.Scale == 7 && view.Matrix.Xx == 1 && view.Matrix.Yy == 1 && view.Matrix.Zz == 1,
                        "Original camera restored after capture");
                    Check(view.WcsVisibility == wcs && view.TriadVisibility == triad, "Restore exact overlay flags");
                    Check(session.Parts.Work == root && session.Parts.Display == root && root.Views.WorkView.Scale == 7,
                        "Original work/display context and camera survive");
                    Check(session.Calls.TrueForAll(c => c.StartsWith("partial ", StringComparison.Ordinal)),
                        "No prototype activation or full load for images");
                    Check(session.LastImageBuilder.Destroyed && File.Exists(session.LastImageBuilder.FileName),
                        "Builder destroyed and test output created");
                }

            Part failedRoot; Component failedComponent;
            Session failedSession = Setup(out failedRoot, out failedComponent);
            View saved = failedRoot.Views.WorkView;
            failedSession.CaptureObserver = view => { throw new IOException("simulated export failure"); };
            Fails<IOException>(() => Capture(failedSession, failedComponent, output, "failure", ImageExporter.SnapViewType.Front),
                "Export error propagates");
            Check(saved.Scale == 7 && saved.WcsVisibility && !saved.TriadVisibility && failedSession.LastImageBuilder.Destroyed &&
                failedSession.Parts.Display == failedRoot, "Export failure restores camera, overlays, context and destroys builder");
            int captured = 0;
            failedSession.CaptureObserver = view => { ++captured; AssemblyTraverser.RequestStop(); };
            Fails<OperationCanceledException>(() => Capture(failedSession, failedComponent, output, "cancel",
                ImageExporter.SnapViewType.Front, ImageExporter.SnapViewType.Top), "Cancellation prevents second image");
            Check(captured == 1 && saved.Scale == 7 && saved.WcsVisibility && !saved.TriadVisibility &&
                failedSession.LastImageBuilder.Destroyed && failedSession.Parts.Display == failedRoot,
                "Cancellation restores view/context and releases builder");

            AssemblyTraverser.Stopped = false;
            var previousBuilder = failedSession.LastImageBuilder;
            failedSession.Parts.BaseWork = (Part)failedComponent.Prototype;
            Fails<InvalidOperationException>(() => new ImageExporter(config.viewList).Process(failedComponent, failedSession, output, "wrong"),
                "Wrong work/display context is rejected");
            Check(failedSession.LastImageBuilder == previousBuilder, "Wrong context never captures another part");
            failedSession.Parts.BaseWork = failedRoot;

            failedSession.CaptureObserver = view => failedSession.LastImageBuilder.SkipOutput = true;
            Fails<IOException>(() => Capture(failedSession, failedComponent, output, "missing", ImageExporter.SnapViewType.Top),
                "Commit without an output file is a failure");
            Check(saved.Scale == 7 && saved.WcsVisibility && !saved.TriadVisibility && failedSession.LastImageBuilder.Destroyed,
                "Missing output still restores view and destroys builder");

            failedSession.CaptureObserver = view => view.FailTransformRestore = true;
            Fails<InvalidOperationException>(() => Capture(failedSession, failedComponent, output, "restore", ImageExporter.SnapViewType.Top),
                "Camera restoration error propagates");
            Check(AssemblyTraverser.Stopped && saved.WcsVisibility && !saved.TriadVisibility &&
                failedSession.Parts.Display == failedRoot, "Camera failure restores overlay flags and stops traversal");
            saved.FailTransformRestore = false;
            var state = new NxViewState(saved);
            saved.FailTransformRestore = true;
            state.Restore(); // Unchanged camera must not be written back.
            Check(true, "Untouched camera is not rewritten");
            CheckReleaseAfterCapture(output);
            VisibilityChecks(output);
            CameraChecks();
        }
        finally { Directory.Delete(output, true); }
        return _checks;
    }
    private static void CheckReleaseAfterCapture(string output)
    {
        Part root; Component component;
        Session session = Setup(out root, out component);
        using (var resources = new NxPartResources(session))
        {
            Capture(session, component, output, "release", ImageExporter.SnapViewType.Top);
            Check(resources.ReleaseUnused("capture restored") && ((Part)component.Prototype).Closed,
                "Used initially clean partial prototype is released after restoring context");
            Check(session.Parts.Items.Count == 1 && !root.Closed, "Initial displayed part is preserved");
        }
    }

    private static void VisibilityChecks(string output)
    {
        Part root; Component target;
        Session session = Setup(out root, out target);
        View view = root.Views.WorkView;
        Component rootComp = root.ComponentAssembly.RootComponent;
        Component sibling = rootComp.Children[1];
        // A nested, originally hidden path, with its own visible geometry.
        Part branchPart = session.Add("ancestor", PartLoadState.FullyLoaded);
        var branch = new Component { Tag = (Tag)2100, Prototype = branchPart, OwningPart = root,
            Parent = rootComp, IsBlanked = true };
        rootComp.Children[0] = branch; branch.Children.Add(target); target.Parent = branch;
        target.IsBlanked = true;
        var hiddenSibling = new Component { Tag = (Tag)2101, Parent = rootComp, IsBlanked = true };
        rootComp.Children.Add(hiddenSibling);
        var hiddenDescendant = new Component { Tag = (Tag)2102, Parent = target, IsBlanked = true };
        target.Children.Add(hiddenDescendant); // Null prototype: must never be opened.
        var siblingInstance = new Component { Tag = (Tag)2103, Prototype = target.Prototype, Parent = branch };
        branch.Children.Add(siblingInstance);
        var ancestorBody = new Body { Tag = (Tag)3100, OwningPart = branchPart, OwningComponent = branch };
        view.Scene.Add(ancestorBody);
        view.Scene.Add(new Body { Tag = (Tag)3101, OwningPart = (Part)target.Prototype, OwningComponent = siblingInstance });
        view.Scene.Add(new Body { Tag = (Tag)3102, OwningComponent = hiddenDescendant });
        session.CaptureObserver = active =>
        {
            Check(sibling.IsBlanked && siblingInstance.IsBlanked && hiddenSibling.IsBlanked,
                "Other occurrences are hidden, including another instance of the same prototype");
            Check(!branch.IsBlanked && !target.IsBlanked && hiddenDescendant.IsBlanked,
                "Show the selected path while preserving hidden descendants");
            Check(ancestorBody.IsBlanked && view.Scene[1].IsBlanked && active.AskVisibleObjects().Length == 1,
                "Exclude ancestor geometry and geometry revealed by fitting, not just siblings");
        };
        Capture(session, target, output, "nested", ImageExporter.SnapViewType.Top, ImageExporter.SnapViewType.Front);
        Check(branch.IsBlanked && target.IsBlanked && hiddenDescendant.IsBlanked && hiddenSibling.IsBlanked &&
            !sibling.IsBlanked && !siblingInstance.IsBlanked && !ancestorBody.IsBlanked && !view.Scene[1].IsBlanked,
            "Restore exact visibility of changed objects; keep originally hidden objects hidden");
        Check(session.Parts.Items.Count == 3 && session.Calls.Count == 1 && session.Calls[0] == "partial readonly prototype",
            "No sibling or hidden descendant load and no prototype activation");

        session = Setup(out root, out target);
        ((Part)target.Prototype).ThrowOnPartialLoad = true;
        sibling = root.ComponentAssembly.RootComponent.Children[1];
        Fails<InvalidOperationException>(() => Capture(session, target, output, "load failure", ImageExporter.SnapViewType.Top),
            "Display load failure propagates after the visibility mask was applied");
        Check(!sibling.IsBlanked && root.Views.WorkView.Scale == 7 && !AssemblyTraverser.Stopped,
            "Constructor failure restores mask and camera without losing the original session");

        session = Setup(out root, out target);
        sibling = root.ComponentAssembly.RootComponent.Children[1];
        bool injected = false;
        session.DisplayManager.AfterChange = (obj, blank) =>
        {
            if (!injected && blank) { injected = true; throw new InvalidOperationException("partial batch failure"); }
        };
        Fails<InvalidOperationException>(() => Capture(session, target, output, "batch failure", ImageExporter.SnapViewType.Top),
            "Partially applied hide batch is handled");
        Check(!sibling.IsBlanked && !AssemblyTraverser.Stopped, "Snapshot predates the failed NX call and permits rollback");

        session = Setup(out root, out target);
        using (var resources = new NxPartResources(session))
        {
            session.DisplayManager.AfterChange = (obj, blank) =>
            {
                if (!blank) throw new InvalidOperationException("restore failure");
            };
            Fails<InvalidOperationException>(() => Capture(session, target, output, "restore failure", ImageExporter.SnapViewType.Top),
                "Visibility restoration failure is not silently ignored");
            resources.ReleaseUnused("unrestored mask");
            Check(AssemblyTraverser.Stopped && !((Part)target.Prototype).Closed && root.Views.WorkView.Scale == 7 &&
                root.Views.WorkView.WcsVisibility, "Failed visibility restore still restores camera/overlays and retains dependencies");
        }

        session = Setup(out root, out target);
        root.Views.ActiveOverride = new[] { root.Views.WorkView, NewView(9000) };
        Fails<InvalidOperationException>(() => Capture(session, target, output, "split", ImageExporter.SnapViewType.Top),
            "Multiple viewports are rejected before changing visibility");
        root.Views.ActiveOverride = null;
        NXOpen.UF.UFSession.Instance.Assem.Explosion = (Tag)8000;
        Fails<InvalidOperationException>(() => Capture(session, target, output, "exploded", ImageExporter.SnapViewType.Top),
            "Exploded view cannot masquerade as an ordinary occurrence transform");
        NXOpen.UF.UFSession.Instance.Assem.Explosion = Tag.Null;
        view = root.Views.WorkView;
        view.Scene.Clear();
        Fails<InvalidOperationException>(() => Capture(session, target, output, "empty", ImageExporter.SnapViewType.Top),
            "Empty reference set/display does not produce a successful blank image");
        Check(session.LastImageBuilder == null && view.Scale == 7, "Preflight/frame failures do not create image builders");

        session = Setup(out root, out target);
        NXOpen.UF.UFSession.Instance.Assem.Transform = new double[,] {
            { 0, 0, 1, 120 }, { 1, 0, 0, -50 }, { 0, 1, 0, 30 }, { 0, 0, 0, 1 } };
        session.CaptureObserver = active =>
        {
            Matrix3x3 m = active.Matrix;
            // The fake canned camera maps local (2,3,5) to (3,-2,5).
            // The occurrence maps the same direction to assembly (5,2,3).
            Check(Math.Abs(m.Xx * 5 + m.Xy * 2 + m.Xz * 3 - 3) < 1e-10 &&
                Math.Abs(m.Yx * 5 + m.Yy * 2 + m.Yz * 3 + 2) < 1e-10 &&
                Math.Abs(m.Zx * 5 + m.Zy * 2 + m.Zz * 3 - 5) < 1e-10,
                "Exporter actually applies prototype ACS camera compensation before capture");
        };
        Capture(session, target, output, "rotated", ImageExporter.SnapViewType.Top);
        Check(root.Views.WorkView.Scale == 7 && root.Views.WorkView.Matrix.Xx == 1,
            "Compensated camera does not become the user's restored camera");
    }

    private static void CameraChecks()
    {
        double[,] camera = { { 0, 0, 1 }, { 0, 1, 0 }, { -1, 0, 0 } };
        // Combined parent + child rotation; translation must not affect directions.
        double[,] transform = { { 0, 0, 1, 120 }, { 1, 0, 0, -50 }, { 0, 1, 0, 30 }, { 0, 0, 0, 1 } };
        double[,] composed = ExportUtilities.PrototypeViewInAssembly(camera, transform);
        double[] local = { 2, 3, 5 }, rotated = { 5, 2, 3 };
        for (int row = 0; row < 3; ++row)
        {
            double expected = 0, actual = 0;
            for (int col = 0; col < 3; ++col) { expected += camera[row, col] * local[col]; actual += composed[row, col] * rotated[col]; }
            Check(Math.Abs(expected - actual) < 1e-10, "Compensated assembly camera preserves prototype ACS direction");
        }
        transform = IdentityTransform(); transform[0, 0] = -1;
        Fails<ArgumentException>(() => ExportUtilities.PrototypeViewInAssembly(camera, transform), "Reject mirrored transforms");
        transform = IdentityTransform(); transform[0, 1] = 0.5;
        Fails<ArgumentException>(() => ExportUtilities.PrototypeViewInAssembly(camera, transform), "Reject shear");
        transform = IdentityTransform(); transform[1, 1] = 2;
        Fails<ArgumentException>(() => ExportUtilities.PrototypeViewInAssembly(camera, transform), "Reject nonuniform scale");
    }
}


namespace NXOpen
{
    public struct Matrix3x3 { public double Xx, Xy, Xz, Yx, Yy, Yz, Zx, Zy, Zz; }
    public struct Point3d { public double X, Y, Z; }
    public interface IFitTo { }
    public class NXObject
    {
        public Tag Tag;
        public Part OwningPart;
        public Component OwningComponent;
    }
    public class DisplayableObject : NXObject
    {
        public bool IsBlanked, OffscreenUntilFit, Occluded;
        public int Layer = 1;
    }
    public sealed class Body : DisplayableObject { }
    public sealed class Face : DisplayableObject { public Body Body; public Body GetBody() { return Body; } }
    public sealed class Edge : DisplayableObject { public Body Body; public Body GetBody() { return Body; } }
    public class View
    {
        public enum Canned { Trimetric, Isometric, Top, Front, Right, Back, Bottom, Left }
        public enum ScaleAdjustment { Fit }
        public Tag Tag;
        public Matrix3x3 Matrix;
        public Point3d Origin;
        public double Scale;
        public bool WcsVisibility, TriadVisibility, FailTransformRestore, Fitted;
        public readonly System.Collections.Generic.List<DisplayableObject> Scene = new System.Collections.Generic.List<DisplayableObject>();
        public void SetRotationTranslationScale(Matrix3x3 matrix, Point3d origin, double scale)
        {
            if (FailTransformRestore) throw new InvalidOperationException("restore failed");
            Matrix = matrix; Origin = origin; Scale = scale;
        }
        public void Orient(Canned canned, ScaleAdjustment fit)
        { Matrix = new Matrix3x3 { Xy = 1, Yx = -1, Zz = 1 }; Scale = 1; }
        public void Orient(Matrix3x3 matrix) { Matrix = matrix; }
        public void Fit() { Fitted = true; Scale = 1; }
        public void FitToObjects(IFitTo[] objects) { Fitted = true; Scale = 1; }
        public DisplayableObject[] AskVisibleObjects()
        {
            var visible = new System.Collections.Generic.List<DisplayableObject>();
            foreach (DisplayableObject obj in Scene)
            {
                if (obj.IsBlanked || obj.Occluded || (obj.OffscreenUntilFit && !Fitted)) continue;
                bool hidden = false;
                for (Component c = obj as Component ?? obj.OwningComponent; c != null; c = c.Parent)
                    if (c.IsBlanked || c.IsSuppressed) { hidden = true; break; }
                if (!hidden) visible.Add(obj);
            }
            return visible.ToArray();
        }
    }
    public sealed class ModelingView : View { }
    public sealed class ViewCollection
    {
        public View WorkView;
        public View[] ActiveOverride;
        public View[] GetActiveViews() { return ActiveOverride ?? new[] { WorkView }; }
    }
    public sealed class DisplayManager
    {
        public Action<DisplayableObject, bool> AfterChange;
        public void MakeUpToDate() { }
        public void BlankObjects(DisplayableObject[] objects) { Change(objects, true); }
        public void UnblankObjects(DisplayableObject[] objects) { Change(objects, false); }
        private void Change(DisplayableObject[] objects, bool blank)
        {
            foreach (DisplayableObject obj in objects)
            {
                obj.IsBlanked = blank;
                if (AfterChange != null) AfterChange(obj, blank);
            }
        }
    }
    public sealed class UI
    {
        public static Session SessionOwner;
        public static UI GetUI() { return new UI(); }
        public Gateway.ImageExportBuilder CreateImageExportBuilder()
        { return SessionOwner.LastImageBuilder = new Gateway.ImageExportBuilder { Owner = SessionOwner }; }
    }
}
namespace NXOpen.Gateway
{
    public sealed class ImageExportBuilder
    {
        public enum FileFormats { Png }
        public enum BackgroundOptions { Transparent }
        public Session Owner;
        public bool RegionMode, EnhanceEdges, Destroyed, SkipOutput;
        public FileFormats FileFormat;
        public string FileName;
        public BackgroundOptions BackgroundOption;
        public void Commit()
        {
            if (Owner.CaptureObserver != null) Owner.CaptureObserver(Owner.Parts.Display.Views.WorkView);
            if (!SkipOutput) File.WriteAllText(FileName, "Fake output: NOT an NX-rendered image");
        }
        public void Destroy() { Destroyed = true; }
    }
}
namespace NXOpen.UF
{
    public sealed class UFSession
    {
        public static readonly UFSession Instance = new UFSession();
        public static UFSession GetUFSession() { return Instance; }
        public readonly UFCsys Csys = new UFCsys();
        public readonly UFModl Modl = new UFModl();
        public readonly UFObj Obj = new UFObj();
        public readonly UFAssem Assem = new UFAssem();
    }
    public sealed class UFAssem
    {
        public Tag Explosion;
        public double[,] Transform;
        public readonly System.Collections.Generic.Dictionary<Tag, NXObject[]> Members = new System.Collections.Generic.Dictionary<Tag, NXObject[]>();
        public void CycleObjsInComp(Tag component, ref Tag member)
        {
            NXObject[] objects;
            if (!Members.TryGetValue(component, out objects)) { member = Tag.Null; return; }
            int next = 0;
            if (member != Tag.Null)
                for (int i = 0; i < objects.Length; ++i) if (objects[i].Tag == member) { next = i + 1; break; }
            member = next < objects.Length ? objects[next].Tag : Tag.Null;
            if (member != Tag.Null) Utilities.NXObjectManager.Objects[member] = objects[next];
        }
        public void AskViewExplosion(Tag view, out Tag explosion) { explosion = Explosion; }
        public void AskTransformOfOcc(Tag occurrence, double[,] result)
        {
            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 4; ++j) result[i, j] = Transform[i, j];
        }
    }
}
