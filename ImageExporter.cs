using System;
using System.IO;
using NXOpen;
using NXOpen.Assemblies;
using NXOpen.Gateway;

public sealed class ImageExporter : ITransactionHandler
{
    public enum SnapViewType { Trimetric, Isometric, Top, Front, Right, Back, Bottom, Left }
    private readonly SnapViewType[] _views;

    public ImageExporter(SnapViewType[] views)
    {
        _views = (SnapViewType[])(views ?? new SnapViewType[0]).Clone();
    }

    public void Process(Component comp, Session session, string outputFolder, string descriptiveName)
    {
        Part part = comp.Prototype as Part;
        if (part == null || session.Parts.Display != part || session.Parts.Work != part)
            throw new InvalidOperationException("截图必须在目标原型部件上下文中执行。");
        // Loads descendant geometry too, avoiding screenshots of partially loaded assemblies.
        if (NxGeometry.CollectBodyTags(part).Count == 0)
            throw new InvalidOperationException("没有可截图实体。");
        ModelingView view = part.ModelingViews.WorkView;
        Matrix3x3 rotation = view.Matrix;
        Point3d translation = view.Origin;
        double scale = view.Scale;
        string stem = ExportUtilities.SafeFileStem(descriptiveName, part.FullPath + "|" + descriptiveName);
        try
        {
            foreach (SnapViewType selected in _views)
            {
                if (AssemblyTraverser.StopRequested) throw new OperationCanceledException("截图已停止。");
                RunLog.Write("[Orient] " + comp.DisplayName + " " + selected);
                view.Orient(ToCanned(selected), View.ScaleAdjustment.Fit);
                string path = Path.Combine(outputFolder, stem + "_" + selected + ".png");
                ExportImage(path);
                RunLog.Write("[Image] " + path);
            }
        }
        finally
        {
            // Do not set WCS.Visibility, change triad preferences, or clean up source geometry.
            // Restore exactly the captured view; restoration failures must be visible.
            RunLog.Write("[Restore view] " + comp.DisplayName);
            try { view.SetRotationTranslationScale(rotation, translation, scale); }
            catch
            {
                AssemblyTraverser.RequestStop();
                throw;
            }
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
            builder.Commit();
        }
        finally { builder.Destroy(); }
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
            throw new IOException("NX 未生成有效图片: " + path);
    }

    public void Finish() { }
}
