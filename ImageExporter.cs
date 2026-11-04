using System;
using System.IO;
using System.Collections.Generic;
using NXOpen;
using NXOpen.Gateway;

public sealed class ImageExporter : ITransactionHandler
{
    public enum SnapViewType { Trimetric, Isometric, Top, Front, Right, Back, Bottom, Left }
    private readonly SnapViewType[] _views;

    public ImageExporter(SnapViewType[] views, bool useIsolatedView = false)
    {
        if (useIsolatedView)
            throw new NotSupportedException("隔离截图已停用，不能创建额外隔离窗口。");
        if (views == null || views.Length == 0) throw new ArgumentException("截图事务必须提供至少一个视角。", "views");
        var unique = new List<SnapViewType>();
        foreach (SnapViewType view in views)
        {
            if (!Enum.IsDefined(typeof(SnapViewType), view)) throw new ArgumentException("无效的截图视角。", "views");
            if (!unique.Contains(view)) unique.Add(view);
        }
        _views = unique.ToArray();
    }

    public void Process(TransactionContext context)
    {
        foreach (SnapViewType selected in _views)
        {
            context.CheckCancellation();
            context.PrepareImageView(ToCanned(selected));
            string path = Path.Combine(context.OutputFolder, context.FileStem + "_" + selected + ".png");
            ExportImage(context, path);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new IOException("NX 未生成有效图片: " + path);
            context.Log("[Image] " + path);
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

    private static void ExportImage(TransactionContext context, string path)
    {
        ImageExportBuilder builder = UI.GetUI().CreateImageExportBuilder();
        try
        {
            builder.RegionMode = false;
            builder.FileFormat = ImageExportBuilder.FileFormats.Png;
            builder.FileName = path;
            builder.BackgroundOption = ImageExportBuilder.BackgroundOptions.Transparent;
            builder.EnhanceEdges = true;
            context.NxCall("Capture assembly view", () => { builder.Commit(); });
        }
        finally { builder.Destroy(); }
    }

    public void Finish() { }
}
