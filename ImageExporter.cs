using System;
using System.IO;
using NXOpen;
using NXOpen.Assemblies;
using NXOpen.Gateway;

public sealed class ImageExporter : ITransactionHandler
{
    public enum SnapViewType { Trimetric, Isometric, Top, Front, Right, Back, Bottom, Left }
    private readonly SnapViewType[] _views;

    public ImageExporter(SnapViewType[] views, bool useIsolatedView = false)
    {
        if (useIsolatedView)
            throw new NotSupportedException("隔离截图已停用，不能创建额外隔离窗口。");
        _views = (SnapViewType[])(views ?? new SnapViewType[0]).Clone();
    }

    public void Process(Component comp, Session session, string outputFolder, string descriptiveName)
    {
        using (var context = new NxAssemblyExportContext(session, comp))
            Process(context, outputFolder, descriptiveName);
    }

    public void Process(NxAssemblyExportContext context, string outputFolder, string descriptiveName)
    {
        Component comp = context.Target;
        Part part = context.TargetPart;
        if (part == null) throw new InvalidOperationException("无法取得截图原型。");
        View view = context.View;
        string stem = ExportUtilities.SafeFileStem(descriptiveName, part.FullPath + "|" + descriptiveName);
        try
        {
            // View-local flags avoid writing part.WCS or global triad settings.
            view.WcsVisibility = false;
            view.TriadVisibility = false;
            RunLog.Write("[Image overlays] WCS=False Triad=False（截图视图）");
            foreach (SnapViewType selected in _views)
            {
                TraversalCancellation.Checkpoint();
                RunLog.Write("[Orient] " + comp.DisplayName + " " + selected);
                view.Orient(ToCanned(selected), View.ScaleAdjustment.Fit);
                Matrix3x3 camera = view.Matrix;
                double[,] matrix = ExportUtilities.PrototypeViewInAssembly(new double[,] {
                    { camera.Xx, camera.Xy, camera.Xz }, { camera.Yx, camera.Yy, camera.Yz },
                    { camera.Zx, camera.Zy, camera.Zz } }, context.Transform);
                view.Orient(new Matrix3x3 { Xx = matrix[0, 0], Xy = matrix[0, 1], Xz = matrix[0, 2],
                    Yx = matrix[1, 0], Yy = matrix[1, 1], Yz = matrix[1, 2],
                    Zx = matrix[2, 0], Zy = matrix[2, 1], Zz = matrix[2, 2] });
                RunLog.NxCall("Prepare assembly image frame", context.PrepareFrame);
                string path = Path.Combine(outputFolder, stem + "_" + selected + ".png");
                ExportImage(path);
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    throw new IOException("NX 未生成有效图片: " + path);
                RunLog.Write("[Image] " + path);
            }
        }
        finally
        {
            RunLog.Write("[Restore view] " + comp.DisplayName);
            // The outer context restores the visibility mask and pre-load camera
            // together, even when preparation/capture/cancellation fails.
        }
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
            RunLog.NxCall("Capture assembly view", () => { builder.Commit(); });
        }
        finally { builder.Destroy(); }
    }

    public void Finish() { }
}
