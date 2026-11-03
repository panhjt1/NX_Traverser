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
    private static Session Setup(out Part root, out Component component)
    {
        AssemblyTraverser.Stopped = false;
        var session = new Session();
        root = session.Add("display root", PartLoadState.FullyLoaded);
        session.Parts.BaseWork = root;
        session.Parts.Displayed.Add(root);
        root.Views = new ViewCollection { WorkView = NewView(1000) };
        Part target = session.Add("readonly prototype", PartLoadState.PartiallyLoaded);
        target.Views = new ViewCollection { WorkView = NewView(1001) };
        component = new Component { Tag = (Tag)2000, OwningPart = root,
            Parent = new Component { OwningPart = root, Prototype = root }, Prototype = target };
        UI.SessionOwner = session;
        return session;
    }
    private static View NewView(int tag)
    {
        return new View { Tag = (Tag)tag, Scale = 7, WcsVisibility = true, TriadVisibility = false,
            Matrix = new Matrix3x3 { Xx = 1, Yy = 1, Zz = 1 } };
    }
    private static void Capture(Session session, Component component, string output, string name,
        params ImageExporter.SnapViewType[] views)
    {
        using (var context = new NxPartContext(session, (Part)component.Prototype))
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
            Check(!config.useIsolatedImageView, "Ordinary capture is the default");
            config.useIsolatedImageView = true;
            Fails<NotSupportedException>(config.Validate, "Old isolation configuration is rejected before traversal");
            Fails<NotSupportedException>(() => new ImageExporter(config.viewList, true), "Direct callers cannot enable isolation");

            foreach (bool wcs in new[] { false, true })
                foreach (bool triad in new[] { false, true })
                {
                    Part root; Component component;
                    Session session = Setup(out root, out component);
                    Part target = (Part)component.Prototype;
                    View view = target.Views.WorkView;
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
                    Check(session.Calls.Count == 3 && session.Calls[1] == "display readonly prototype" &&
                        session.Calls[2] == "display display root", "One target activation and one restoration");
                    Check(session.LastImageBuilder.Destroyed && File.Exists(session.LastImageBuilder.FileName),
                        "Builder destroyed and test output created");
                }

            Part failedRoot; Component failedComponent;
            Session failedSession = Setup(out failedRoot, out failedComponent);
            View saved = ((Part)failedComponent.Prototype).Views.WorkView;
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
            Fails<InvalidOperationException>(() => new ImageExporter(config.viewList).Process(failedComponent, failedSession, output, "wrong"),
                "Wrong work/display context is rejected");
            Check(failedSession.LastImageBuilder == previousBuilder, "Wrong context never captures another part");

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
}

namespace NXOpen
{
    public struct Matrix3x3 { public double Xx, Xy, Xz, Yx, Yy, Yz, Zx, Zy, Zz; }
    public struct Point3d { public double X, Y, Z; }
    public sealed class View
    {
        public enum Canned { Trimetric, Isometric, Top, Front, Right, Back, Bottom, Left }
        public enum ScaleAdjustment { Fit }
        public Tag Tag;
        public Matrix3x3 Matrix;
        public Point3d Origin;
        public double Scale;
        public bool WcsVisibility, TriadVisibility, FailTransformRestore;
        public void SetRotationTranslationScale(Matrix3x3 matrix, Point3d origin, double scale)
        {
            if (FailTransformRestore) throw new InvalidOperationException("restore failed");
            Matrix = matrix; Origin = origin; Scale = scale;
        }
        public void Orient(Canned canned, ScaleAdjustment fit)
        { Matrix = new Matrix3x3 { Xy = 1, Yx = -1, Zz = 1 }; Scale = 1; }
    }
    public sealed class ViewCollection { public View WorkView; }
    public sealed class DisplayManager { public void MakeUpToDate() { } }
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
