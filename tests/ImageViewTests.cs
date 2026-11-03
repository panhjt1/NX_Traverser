// Control-flow doubles only: these do not simulate NX rendering, isolate-view
// lifecycle, Teamcenter callbacks, or prove compatibility with Siemens binaries.
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
    private static void Fails(Action action, string message)
    {
        bool failed = false;
        try { action(); } catch (Exception) { failed = true; }
        Check(failed, message);
    }
    private static Session Setup(out Part root, out Component component)
    {
        AssemblyTraverser.Stopped = false;
        var session = new Session();
        root = session.Add("display root", PartLoadState.FullyLoaded);
        session.Parts.BaseWork = root;
        session.Parts.Displayed.Add(root);
        root.Views = new ViewCollection { Owner = root };
        var original = new ModelingView { Owner = root, Tag = (Tag)1000, Scale = 7,
            WcsVisibility = true, TriadVisibility = false, Matrix = new Matrix3x3 { Xx = 1, Yy = 1, Zz = 1 } };
        root.Views.WorkView = original;
        root.Views.Active = new View[] { original };
        root.Layouts = new LayoutCollection { Current = new Layout { Owner = root, Tag = (Tag)1001, Original = original } };
        root.ComponentAssembly = new ComponentAssembly { Owner = root };
        component = new Component { Tag = (Tag)2000, OwningPart = root,
            Parent = new Component { OwningPart = root, Prototype = root },
            Prototype = session.Add("readonly prototype", PartLoadState.PartiallyLoaded) };
        return session;
    }
    public static int Run()
    {
        string output = Path.Combine(Path.GetTempPath(), "nx-image-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            foreach (bool wcs in new[] { false, true })
                foreach (bool triad in new[] { false, true })
                {
                    Part root; Component component;
                    Session session = Setup(out root, out component);
                    View original = root.Views.WorkView;
                    original.WcsVisibility = wcs; original.TriadVisibility = triad;
                    session.CaptureObserver = view => Check(!view.WcsVisibility && !view.TriadVisibility, "Both overlays hidden during capture");
                    var exporter = new ImageExporter(new[] { ImageExporter.SnapViewType.Trimetric });
                    Check(!exporter.RequiresPrototypeContext, "Default images do not activate prototypes");
                    exporter.Process(component, session, output, "image");
                    Check(root.Views.WorkView == original && root.Views.Active[0] == original && original.Scale == 7,
                        "Original view restored after isolate");
                    Check(original.WcsVisibility == wcs && original.TriadVisibility == triad, "Restore exact overlay flags");
                    Check(session.Parts.Work == root && session.Parts.Display == root && session.Calls.Count == 0,
                        "No work/display part switch or full-load adapter call");
                    Check(root.Views.LastBuilder.Destroyed, "Capture builder destroyed");
                }

            Part failedRoot; Component failedComponent;
            Session failedSession = Setup(out failedRoot, out failedComponent);
            View saved = failedRoot.Views.WorkView;
            failedSession.CaptureObserver = view => { throw new IOException("simulated export failure"); };
            var one = new ImageExporter(new[] { ImageExporter.SnapViewType.Front });
            Fails(() => one.Process(failedComponent, failedSession, output, "failure"), "Export error propagates");
            Check(failedRoot.Views.WorkView == saved && saved.WcsVisibility && !saved.TriadVisibility &&
                failedRoot.Views.LastBuilder.Destroyed, "Failure restores view, overlays and builder");
            failedSession.CaptureObserver = view => AssemblyTraverser.RequestStop();
            var two = new ImageExporter(new[] { ImageExporter.SnapViewType.Front, ImageExporter.SnapViewType.Top });
            Fails(() => two.Process(failedComponent, failedSession, output, "cancel"), "Cancellation prevents second image");
            Check(failedRoot.Views.WorkView == saved && saved.WcsVisibility && !saved.TriadVisibility, "Cancel restores overlays and original view");

            AssemblyTraverser.Stopped = false;
            failedRoot.ComponentAssembly.FailAfterCreate = true;
            Fails(() => new NxImageContext(failedSession, failedComponent), "Partial isolate creation failure propagates");
            Check(failedRoot.Views.WorkView == saved && failedRoot.Views.Active[0] == saved, "Partial isolate creation is restored");
            failedRoot.ComponentAssembly.FailAfterCreate = false;
            Layout originalLayout = failedRoot.Layouts.Current;
            failedRoot.Layouts.Current.LeaveIsolateOnOpen = true;
            using (var context = new NxImageContext(failedSession, failedComponent))
                failedRoot.Layouts.Current = new Layout { Owner = failedRoot, Tag = (Tag)1003, Original = saved };
            Check(failedRoot.Views.WorkView == saved && failedRoot.Views.Active[0] == saved, "Layout replacement fallback restores original view");
            Check(failedRoot.Layouts.Current == originalLayout && originalLayout.OpenCalls == 1,
                "A different current layout still requires opening the captured layout");

            // Restoring a camera may fail; overlay restoration must still run.
            var state = new NxViewState(saved);
            saved.FailTransformRestore = true;
            state.Restore(); // Unchanged camera must not be written back.
            Check(true, "Untouched source view is not rewritten");
            saved.Scale = 99;
            saved.WcsVisibility = false; saved.TriadVisibility = true; saved.FailTransformRestore = true;
            Fails(() => state.Restore(), "Camera restoration error is not swallowed");
            Check(saved.WcsVisibility && !saved.TriadVisibility, "Overlay restoration attempted even after camera failure");
            saved.FailTransformRestore = false;

            Part keptRoot; Component keptComponent;
            Session keptSession = Setup(out keptRoot, out keptComponent);
            // Start ownership tracking before opening the temporary target.
            keptSession.Parts.Items.Remove((BasePart)keptComponent.Prototype);
            using (var resources = new NxPartResources(keptSession))
            {
                keptSession.Parts.Items.Add((BasePart)keptComponent.Prototype);
                var context = new NxImageContext(keptSession, keptComponent);
                keptRoot.Layouts.Current.FailReplace = true;
                Fails(context.Dispose, "Failed layout restore propagates");
                Check(AssemblyTraverser.Stopped, "Failed restoration stops further loading");
                resources.ReleaseUnused("failed image restore");
                Check(!((BasePart)keptComponent.Prototype).Closed, "Parts referenced by an unrestored view stay loaded");
            }
            CheckRepeatedIsolateCleanup();
            TransformChecks();
        }
        finally { Directory.Delete(output, true); }
        return _checks;
    }
    private static void CheckRepeatedIsolateCleanup()
    {
        Part root; Component component;
        Session session = Setup(out root, out component);
        Layout layout = root.Layouts.Current;
        View original = root.Views.WorkView;
        session.Parts.Items.Remove((BasePart)component.Prototype);
        using (var resources = new NxPartResources(session))
        {
            for (int i = 0; i < 2; ++i)
            {
                Part temporary = session.Add("temporary image target", PartLoadState.PartiallyLoaded);
                component.Prototype = temporary;
                using (var context = new NxImageContext(session, component)) { }
                Check(layout.OpenCalls == 0 && layout.ReplaceCalls == i + 1,
                    "Current layout is not opened again; isolate view is replaced on each run");
                Check(root.Views.WorkView == original && root.Views.Active[0] == original && !AssemblyTraverser.Stopped,
                    "Repeated isolation restores the original view without a false stop");
                Check(resources.ReleaseUnused("restored isolate") && temporary.Closed && session.Parts.Items.Count == 1,
                    "Successful layout restoration permits timely temporary-part release");
            }
        }
    }
    private static void TransformChecks()
    {
        double[,] camera = { { 0, 0, 1 }, { 0, 1, 0 }, { -1, 0, 0 } };
        double[,] occurrence = { { 0, -2, 0, 321 }, { 2, 0, 0, -777 }, { 0, 0, 2, 456 }, { 0, 0, 0, 1 } };
        double[,] corrected = ExportUtilities.PrototypeViewInAssembly(camera, occurrence);
        // C' * R = C for each basis vector; camera and occurrence rotate about different axes.
        bool matches = true;
        for (int r = 0; r < 3; ++r)
            for (int c = 0; c < 3; ++c)
            {
                double actual = 0;
                for (int k = 0; k < 3; ++k) actual += corrected[r, k] * occurrence[k, c] / 2;
                matches &= Math.Abs(actual - camera[r, c]) < 1e-10;
            }
        Check(matches, "Occurrence rotation, translation and uniform scale preserve prototype camera basis");
        occurrence[0, 1] = 2;
        Fails(() => ExportUtilities.PrototypeViewInAssembly(camera, occurrence), "Reject reflection");
        occurrence[0, 1] = -4;
        Fails(() => ExportUtilities.PrototypeViewInAssembly(camera, occurrence), "Reject nonuniform scale");
        occurrence[0, 1] = -2; occurrence[0, 0] = 1;
        Fails(() => ExportUtilities.PrototypeViewInAssembly(camera, occurrence), "Reject shear");
        occurrence[0, 0] = double.NaN;
        Fails(() => ExportUtilities.PrototypeViewInAssembly(camera, occurrence), "Reject invalid transform");
    }
}

namespace NXOpen
{
    public struct Matrix3x3 { public double Xx, Xy, Xz, Yx, Yy, Yz, Zx, Zy, Zz; }
    public struct Point3d { public double X, Y, Z; }
    public class View
    {
        public enum Canned { Trimetric, Isometric, Top, Front, Right, Back, Bottom, Left }
        public enum ScaleAdjustment { Fit }
        public Part Owner;
        public Tag Tag;
        public Matrix3x3 Matrix;
        public Point3d Origin;
        public double Scale;
        public bool WcsVisibility, TriadVisibility, FailTransformRestore;
        public void MakeWork() { Owner.Views.WorkView = this; }
        public void Expand() { Owner.Views.Expanded = true; MakeWork(); }
        public void SetRotationTranslationScale(Matrix3x3 matrix, Point3d origin, double scale)
        {
            if (FailTransformRestore) throw new Exception("restore failed");
            Matrix = matrix; Origin = origin; Scale = scale;
        }
        public void Orient(Canned canned, ScaleAdjustment fit) { Matrix = new Matrix3x3 { Xx = 1, Yy = 1, Zz = 1 }; Scale = 1; }
        public void Orient(Matrix3x3 matrix) { Matrix = matrix; }
        public void Fit() { Scale = 1; }
    }
    public sealed class ModelingView : View { }
    public sealed class ViewCollection
    {
        public Part Owner;
        public View WorkView;
        public View[] Active;
        public bool Expanded;
        public Display.StudioImageCaptureBuilder LastBuilder;
        public View[] GetActiveViews() { return Active; }
        public bool IsWorkViewExpanded() { return Expanded; }
        public void UnexpandWork() { Expanded = false; }
        public Display.StudioImageCaptureBuilder CreateStudioImageCaptureBuilder()
        { return LastBuilder = new Display.StudioImageCaptureBuilder { Owner = Owner }; }
    }
    public sealed class LayoutCollection { public Layout Current; }
    public sealed class Layout
    {
        public Part Owner;
        public Tag Tag;
        public View Original;
        public bool FailOpen, LeaveIsolateOnOpen, FailReplace;
        public int OpenCalls, ReplaceCalls;
        public View[] GetViews() { return new[] { Original }; }
        public void Open()
        {
            ++OpenCalls;
            // Observed in NX 2512: Open is not idempotent for the current layout.
            if (Owner.Layouts.Current != null && Owner.Layouts.Current.Tag == Tag)
                throw new NXException(925052);
            if (FailOpen) throw new Exception("layout open failed");
            Owner.Layouts.Current = this;
            if (!LeaveIsolateOnOpen) Owner.Views.Active = new[] { Original };
        }
        public void ReplaceView(ModelingView from, ModelingView to, bool fit)
        {
            ++ReplaceCalls;
            if (FailReplace) throw new Exception("layout replace failed");
            Owner.Views.Active = new View[] { to };
        }
    }
    public sealed class DisplayManager { public void MakeUpToDate() { } }
    public sealed class ErrorInfo { public int ErrorCode = 1; public string Description = "test error"; }
    public sealed class ErrorList : IDisposable
    {
        public int Length;
        public ErrorInfo GetErrorInfo(int index) { return new ErrorInfo(); }
        public void Dispose() { }
    }
    public sealed class UI
    {
        public static UI GetUI() { return new UI(); }
        public Gateway.ImageExportBuilder CreateImageExportBuilder() { return new Gateway.ImageExportBuilder(); }
    }
}
namespace NXOpen.Assemblies
{
    public sealed class ComponentAssembly
    {
        public Part Owner;
        public bool FailAfterCreate;
        public ErrorList CreateIsolateViewWithComponents(Component[] components, out View isolated)
        {
            isolated = new ModelingView { Owner = Owner, Tag = (Tag)1002, WcsVisibility = true,
                TriadVisibility = true, Matrix = new Matrix3x3 { Xx = 1, Yy = 1, Zz = 1 } };
            Owner.Views.Active = new[] { isolated };
            return new ErrorList { Length = FailAfterCreate ? 1 : 0 };
        }
    }
}
namespace NXOpen.UF
{
    public sealed class UFSession
    {
        public readonly UFAssem Assem = new UFAssem();
        public static UFSession GetUFSession() { return new UFSession(); }
    }
    public sealed class UFAssem
    {
        public void AskViewExplosion(Tag view, out Tag explosion) { explosion = Tag.Null; }
        public void AskTransformOfOcc(Tag component, double[,] matrix)
        { for (int r = 0; r < 4; ++r) for (int c = 0; c < 4; ++c) matrix[r, c] = r == c ? 1 : 0; }
    }
}
namespace NXOpen.Display
{
    public sealed class StudioImageCaptureBuilder
    {
        public enum SourceType { WorkView }
        public enum DrawingSizeEnumType { Custom }
        public enum UnitsEnumType { Pixels }
        public enum BackgroundOptions { Transparent }
        public Part Owner;
        public SourceType Source;
        public string NativeFileBrowser;
        public DrawingSizeEnumType DrawingSizeEnum;
        public UnitsEnumType UnitsEnum;
        public BackgroundOptions BackgroundOption;
        public bool EnhanceEdges, Destroyed;
        public void SetImageDimensionsInteger(int[] dimensions) { }
        public void Commit()
        {
            if (Owner.Owner.CaptureObserver != null) Owner.Owner.CaptureObserver(Owner.Views.WorkView);
            File.WriteAllText(NativeFileBrowser, "Fake output: NOT an NX-rendered image");
        }
        public void Destroy() { Destroyed = true; }
    }
}
namespace NXOpen.Gateway
{
    public sealed class ImageExportBuilder
    {
        public enum FileFormats { Png }
        public enum BackgroundOptions { Transparent }
        public bool RegionMode, EnhanceEdges;
        public FileFormats FileFormat;
        public string FileName;
        public BackgroundOptions BackgroundOption;
        public void Commit() { File.WriteAllText(FileName, "Fake legacy output"); }
        public void Destroy() { }
    }
}
