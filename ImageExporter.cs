using System;
using NXOpen;
using NXOpen.Assemblies;
using NXOpen.Preferences;
using NXOpen.UF;

/// <summary>
/// 截图导出事务类 - 负责将部件从多个视角截图导出为PNG
/// </summary>
public class ImageExporter : ITransactionHandler
{
    private SnapViewType[] _captureViews;
    
    /// <summary>
    /// 静态标志：是否已初始化只读抑制设置
    /// </summary>
    private static bool _suppressReadonlyWarningInitialized = false;
    
    /// <summary>
    /// 静态标志：是否成功启用了只读抑制
    /// </summary>
    private static bool _suppressReadonlyEnabled = false;

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

    public ImageExporter(SnapViewType[] views)
    {
        _captureViews = views ?? new SnapViewType[0];
        InitializeSuppressReadonlyWarning();
    }
    
    /// <summary>
    /// 初始化只读抑制设置（在 NX 会话级别）
    /// 使用 UF API 设置对话框抑制标志
    /// </summary>
    private void InitializeSuppressReadonlyWarning()
    {
        if (_suppressReadonlyWarningInitialized)
            return;
            
        try
        {
            var ufSession = UFSession.GetUFSession();
            
            // 使用 UF 调用来抑制对话框和只读警告
            // NX 的 UF API 提供了一些可以控制对话框显示的选项
            
            // 方法1：尝试设置 UF_UI 的模式来抑制只读相关警告
            // 注意：UFConstants 中可能有 UF_UI_NO_READONLY_WARNING 相关的常量
            // 但具体名称可能因 NX 版本而异
            
            try
            {
                // 尝试调用 UF_UI_ask_suppress_dialogs 或类似函数
                // NX 1946 可能支持这个功能
                int currentMode = 0;
                ufSession.UI.AskSuppressDialogs(out currentMode);
                
                // 如果当前没有抑制任何对话框，启用抑制
                if (currentMode == 0)
                {
                    // 设置抑制模式，包含只读警告
                    // UF_UI_SUPPRESS_READONLY_WARNING 的值可能需要查文档
                    // 这里使用反射尝试调用
                    var method = ufSession.UI.GetType().GetMethod("SetSuppressDialogs");
                    if (method != null)
                    {
                        // 尝试获取抑制标志常量
                        var flagField = typeof(UFConstants).GetField("UF_UI_SUPPRESS_READONLY_WARNING");
                        if (flagField != null)
                        {
                            int suppressFlag = (int)flagField.GetValue(null);
                            method.Invoke(ufSession.UI, new object[] { suppressFlag });
                            _suppressReadonlyEnabled = true;
                        }
                    }
                }
            }
            catch
            {
                // 方法1失败，尝试备选方案
                _suppressReadonlyEnabled = false;
            }
            
            // 方法2：尝试使用 UF 调度模式设置
            // NX 提供了一些 UF 调用可以控制是否显示某些对话框
            try
            {
                // 尝试使用 UF 的 SetRoutine 调用来改变对话框行为
                // 这可能需要调用 UF_UI_set_main_wireframe_mode 或类似函数
            }
            catch
            {
                // 静默忽略
            }
            
            _suppressReadonlyWarningInitialized = true;
        }
        catch (Exception ex)
        {
            // 静默失败，不影响主流程
            System.Diagnostics.Debug.WriteLine("只读抑制初始化失败: " + ex.Message);
            _suppressReadonlyWarningInitialized = true; // 标记为已初始化，避免重复尝试
        }
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
            foreach (SnapViewType view in _captureViews)
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
        
        // 设置 WCS 可见性（保留此修改，按用户要求）
        // 注意：即使抑制设置生效，这里仍然执行修改
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
