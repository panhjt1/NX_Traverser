using System;
using NXOpen;
using NXOpen.Assemblies;
using NXOpen.Preferences;

/// <summary>
/// 截图导出事务类 - 负责将部件从多个视角截图导出为PNG
/// </summary>
public class ImageExporter : ITransactionHandler
{
    // ========== 配置区域：在这里修改截图视角 ==========
    private static readonly SnapViewType[] CaptureViews =
    {
        SnapViewType.Trimetric,
        SnapViewType.Isometric,
        SnapViewType.Top,
        SnapViewType.Front,
        SnapViewType.Right,
        SnapViewType.Back,
        SnapViewType.Bottom,
        SnapViewType.Left
    };
    // ================================================================

    /// <summary>
    /// 定义所有截图视角的枚举值
    /// </summary>
    public enum SnapViewType
    {
        Trimetric,
        Isometric,
        Top,
        Front,
        Right,
        Back,
        Bottom,
        Left
    }

    /// <summary>
    /// 处理截图导出事务
    /// </summary>
    /// <param name="comp">当前组件</param>
    /// <param name="theSession">NX会话</param>
    /// <param name="outputFolder">输出文件夹路径</param>
    /// <param name="descriptiveName">描述性名称（用于生成文件名）</param>
    public void Process(Component comp, Session theSession, string outputFolder, string descriptiveName)
    {
        Part part = comp.Prototype as Part;
        if (part == null)
        {
            theSession.ListingWindow.WriteLine(string.Format("    【错误】无法获取零件原型: {0}", comp.DisplayName));
            return;
        }

        // 保存原始显示部件，用于事后恢复
        Part originalDisplayPart = theSession.Parts.Display;

        try
        {
            // 将当前零件设为显示部件
            PartLoadStatus loadStatus;
            theSession.Parts.SetDisplay(part, false, false, out loadStatus);

            // 使用传入的友好名称作为基础文件名，并清理非法字符
            string baseName = descriptiveName;
            char[] invalidChars = System.IO.Path.GetInvalidFileNameChars();
            string safeBaseName = string.Join("_", baseName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries))
                                       .Replace('/', '_').Replace('\\', '_')
                                       .TrimEnd(' ', '.');
            if (string.IsNullOrWhiteSpace(safeBaseName))
                safeBaseName = "Unnamed";

            System.IO.Directory.CreateDirectory(outputFolder); // 确保目录存在

            // 遍历配置的视角，逐个截图
            foreach (SnapViewType view in CaptureViews)
            {
                SetView(part, view);
                string viewName = view.ToString();
                string fileName = System.IO.Path.Combine(outputFolder, safeBaseName + "_" + viewName + ".png");
                ExportImage(part, fileName, theSession);
                theSession.ListingWindow.WriteLine(string.Format("      {0} -> {1}", viewName, fileName));
            }
        }
        catch (Exception ex)
        {
            theSession.ListingWindow.WriteLine(string.Format("    【截图失败】{0}: {1}", comp.DisplayName, ex.Message));
        }
        finally
        {
            // 恢复原始显示部件
            if (originalDisplayPart != null && originalDisplayPart != part)
            {
                try
                {
                    theSession.Parts.SetDisplay(originalDisplayPart, false, false, out PartLoadStatus restoreStatus);
                }
                catch { /* 忽略恢复错误 */ }
            }
        }
    }

    /// <summary>
    /// 事务结束后的资源清理（截图事务无需释放资源）
    /// </summary>
    public void Finish()
    {
        // 截图事务无需释放资源
    }

    /// <summary>
    /// 设置视图方向
    /// </summary>
    private void SetView(Part part, SnapViewType viewType)
    {
        switch (viewType)
        {
            case SnapViewType.Trimetric:
                part.ModelingViews.WorkView.Orient(NXOpen.View.Canned.Trimetric, NXOpen.View.ScaleAdjustment.Fit);
                break;
            case SnapViewType.Isometric:
                part.ModelingViews.WorkView.Orient(NXOpen.View.Canned.Isometric, NXOpen.View.ScaleAdjustment.Fit);
                break;
            case SnapViewType.Top:
                part.ModelingViews.WorkView.Orient(NXOpen.View.Canned.Top, NXOpen.View.ScaleAdjustment.Fit);
                break;
            case SnapViewType.Front:
                part.ModelingViews.WorkView.Orient(NXOpen.View.Canned.Front, NXOpen.View.ScaleAdjustment.Fit);
                break;
            case SnapViewType.Right:
                part.ModelingViews.WorkView.Orient(NXOpen.View.Canned.Right, NXOpen.View.ScaleAdjustment.Fit);
                break;
            case SnapViewType.Back:
                part.ModelingViews.WorkView.Orient(NXOpen.View.Canned.Back, NXOpen.View.ScaleAdjustment.Fit);
                break;
            case SnapViewType.Bottom:
                part.ModelingViews.WorkView.Orient(NXOpen.View.Canned.Bottom, NXOpen.View.ScaleAdjustment.Fit);
                break;
            case SnapViewType.Left:
                part.ModelingViews.WorkView.Orient(NXOpen.View.Canned.Left, NXOpen.View.ScaleAdjustment.Fit);
                break;
            default:
                part.ModelingViews.WorkView.Orient(NXOpen.View.Canned.Isometric, NXOpen.View.ScaleAdjustment.Fit);
                break;
        }
        part.ModelingViews.WorkView.Fit();
    }

    /// <summary>
    /// 导出单张图片
    /// </summary>
    private void ExportImage(Part part, string fileName, Session theSession)
    {
        // 确保路径合法
        string dir = System.IO.Path.GetDirectoryName(fileName);
        string nameOnly = System.IO.Path.GetFileNameWithoutExtension(fileName);
        string safeName = string.Join("_", nameOnly.Split(System.IO.Path.GetInvalidFileNameChars()))
                               .Replace('/', '_').Replace('\\', '_')
                               .TrimEnd(' ', '.');
        string safeFileName = System.IO.Path.Combine(dir, safeName + ".png");

        // 如果已有同名文件，尝试删除
        if (System.IO.File.Exists(safeFileName))
        {
            try
            {
                System.IO.File.SetAttributes(safeFileName, System.IO.FileAttributes.Normal);
                System.IO.File.Delete(safeFileName);
            }
            catch { /* 无法删除则继续，Commit 可能会失败 */ }
        }

        var screenVis = theSession.Preferences.ScreenVisualization;
        var theUI = NXOpen.UI.GetUI();
        var imageBuilder = theUI.CreateImageExportBuilder();
        screenVis.TriadVisibility = 0;
        part.WCS.Visibility = false;
        try
        {
            imageBuilder.RegionMode = false;
            imageBuilder.SetRegionTopLeftPoint(new int[] { 0, 0 });
            imageBuilder.RegionWidth = 1;
            imageBuilder.RegionHeight = 1;
            imageBuilder.FileFormat = NXOpen.Gateway.ImageExportBuilder.FileFormats.Png;
            imageBuilder.FileName = safeFileName;
            imageBuilder.BackgroundOption = NXOpen.Gateway.ImageExportBuilder.BackgroundOptions.Transparent;
            imageBuilder.EnhanceEdges = true;

            NXOpen.NXObject nXObject = imageBuilder.Commit();
            imageBuilder.Destroy();
            theSession.CleanUpFacetedFacesAndEdges();
        }
        finally
        {
            part.WCS.Visibility = true;
            screenVis.TriadVisibility = 1;
        }
    }
}
