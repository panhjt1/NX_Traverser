using System;
using System.IO;
using NXOpen;
using NXOpen.Assemblies;
using NXOpen.Gateway;

public sealed class ImageExporter : ITransactionHandler
{
    public enum SnapViewType { Trimetric, Isometric, Top, Front, Right, Back, Bottom, Left }
    private readonly SnapViewType[] _views;
    private readonly bool _isolated;
    public bool RequiresPrototypeContext { get { return !_isolated; } }

    public ImageExporter(SnapViewType[] views, bool useIsolatedView = true)
    {
        _views = (SnapViewType[])(views ?? new SnapViewType[0]).Clone();
        _isolated = useIsolatedView;
    }

    public void Process(Component comp, Session session, string outputFolder, string descriptiveName)
    {
        Part part = comp.Prototype as Part;
        if (part == null) throw new InvalidOperationException("无法取得截图原型。");
        if (_isolated)
        {
            using (var context = new NxImageContext(session, comp))
            {
                RunLog.Write("[Image context] 临时隔离组件；不激活只读原型: " + comp.DisplayName);
                NxGeometry.PrepareDisplay(part);
                Capture(comp, part, context.View, session, outputFolder, descriptiveName, context.Transform);
            }
            return;
        }
        if (session.Parts.Display != part || session.Parts.Work != part)
            throw new InvalidOperationException("截图必须在目标原型部件上下文中执行。");
        RunLog.Write("[Load display] " + comp.DisplayName + "（后代仅请求显示数据，不逐个完整加载）");
        NxGeometry.PrepareDisplay(part);
        Capture(comp, part, part.ModelingViews.WorkView, session, outputFolder, descriptiveName, null);
    }

    private void Capture(Component comp, Part part, View view, Session session,
        string outputFolder, string descriptiveName, double[,] occurrenceTransform)
    {
        var state = new NxViewState(view);
        string stem = ExportUtilities.SafeFileStem(descriptiveName, part.FullPath + "|" + descriptiveName);
        try
        {
            // View-local flags avoid writing part.WCS or global triad settings.
            view.WcsVisibility = false;
            view.TriadVisibility = false;
            RunLog.Write("[Image overlays] WCS=False Triad=False（截图视图）");
            foreach (SnapViewType selected in _views)
            {
                if (AssemblyTraverser.StopRequested) throw new OperationCanceledException("截图已停止。");
                RunLog.Write("[Orient] " + comp.DisplayName + " " + selected);
                view.Orient(ToCanned(selected), View.ScaleAdjustment.Fit);
                if (occurrenceTransform != null)
                {
                    Matrix3x3 camera = view.Matrix;
                    double[,] matrix = ExportUtilities.PrototypeViewInAssembly(new double[,] {
                        { camera.Xx, camera.Xy, camera.Xz }, { camera.Yx, camera.Yy, camera.Yz },
                        { camera.Zx, camera.Zy, camera.Zz } }, occurrenceTransform);
                    view.Orient(new Matrix3x3 { Xx = matrix[0, 0], Xy = matrix[0, 1], Xz = matrix[0, 2],
                        Yx = matrix[1, 0], Yy = matrix[1, 1], Yz = matrix[1, 2],
                        Zx = matrix[2, 0], Zy = matrix[2, 1], Zz = matrix[2, 2] });
                    view.Fit();
                }
                session.DisplayManager.MakeUpToDate();
                string path = Path.Combine(outputFolder, stem + "_" + selected + ".png");
                if (_isolated) ExportWorkView(session, path);
                else ExportImage(path);
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    throw new IOException("NX 未生成有效图片: " + path);
                RunLog.Write("[Image] " + path);
            }
        }
        finally
        {
            RunLog.Write("[Restore view] " + comp.DisplayName);
            try { state.Restore(); }
            catch
            {
                AssemblyTraverser.RequestStop();
                throw;
            }
        }
    }

    private static void ExportWorkView(Session session, string path)
    {
        var builder = session.Parts.Display.Views.CreateStudioImageCaptureBuilder();
        try
        {
            // Capture ONLY the isolated work view, not other graphics viewports.
            builder.Source = NXOpen.Display.StudioImageCaptureBuilder.SourceType.WorkView;
            builder.NativeFileBrowser = path;
            builder.DrawingSizeEnum = NXOpen.Display.StudioImageCaptureBuilder.DrawingSizeEnumType.Custom;
            builder.UnitsEnum = NXOpen.Display.StudioImageCaptureBuilder.UnitsEnumType.Pixels;
            builder.SetImageDimensionsInteger(new[] { 1200, 1600 }); // height, width
            builder.BackgroundOption = NXOpen.Display.StudioImageCaptureBuilder.BackgroundOptions.Transparent;
            builder.EnhanceEdges = true;
            RunLog.NxCall("Capture isolated work view", () => { builder.Commit(); });
        }
        finally { builder.Destroy(); }
    }

    private static View.Canned ToCanned(SnapViewType view)
    {
        switch (view)
        {
            case SnapViewType.Trimetric: return View.Canned.Trimetric;
            case SnapViewType.Isometric: return View.Canned.Isometric;
            case SnapViewType.Top: return View.Canned.Top;
            case SnapViewType.Front: return View.Canned.Front;
            case SnapViewType.Right: return View.Canned.Right;
            case SnapViewType.Back: return View.Canned.Back;
            case SnapViewType.Bottom: return View.Canned.Bottom;
            case SnapViewType.Left: return View.Canned.Left;
            default: throw new ArgumentOutOfRangeException("view");
        }
    }

    private static void ExportImage(string path)
    {
        ImageExportBuilder builder = UI.GetUI().CreateImageExportBuilder();
        try
        {
            builder.RegionMode = false;
            builder.FileFormat = ImageExportBuilder.FileFormats.Png;
            builder.FileName = path;
            builder.BackgroundOption = ImageExportBuilder.BackgroundOptions.Transparent;
            builder.EnhanceEdges = true;
            builder.Commit();
        }
        finally { builder.Destroy(); }
    }

    public void Finish() { }
}
