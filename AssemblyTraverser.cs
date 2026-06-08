using System;
using NXOpen;
using NXOpen.Assemblies;
using NXOpen.UF;

/// <summary>
/// 装配遍历器主类 - 负责遍历装配结构树并调用事务处理器
/// </summary>
public class AssemblyTraverser
{
    private static TraversalConfig _config;
    private static ITransactionHandler[] _transactionHandlers;
    private static readonly int MaxLevel = 5;

    public static void Run(TraversalConfig config)
    {
        _config = config ?? new TraversalConfig();
        BuildTransactionHandlers();
        
        InternalMain();
    }

    private static void BuildTransactionHandlers()
    {
        System.Collections.Generic.List<ITransactionHandler> handlers = 
            new System.Collections.Generic.List<ITransactionHandler>();

        if (_config.flagImageOn && _config.viewList != null && _config.viewList.Length > 0)
        {
            handlers.Add(new ImageExporter(_config.viewList));
        }

        if (_config.flagXYZOn)
        {
            handlers.Add(new BoundingBoxExporter(_config.coordinateSelection));
        }

        _transactionHandlers = handlers.ToArray();
    }

    private static void InternalMain()
    {
        // 重置停止标记
        StopForm.StopRequested = false;

        // 启动自己的控制窗口（非模态）
        StopForm stopForm = new StopForm();
        stopForm.Show();   // 非模态，不阻塞

        var theSession = Session.GetSession();
        var ufSession = UFSession.GetUFSession();
        Part workPart = theSession.Parts.Work;
        theSession.ListingWindow.Open();

        if (workPart == null)
        {
            theSession.ListingWindow.WriteLine("Error: No work part found.");
            return;
        }

        Component rootComponent = workPart.ComponentAssembly.RootComponent;
        if (rootComponent == null)
        {
            theSession.ListingWindow.WriteLine("Error: Root component is null. Possibly a piece part.");
            return;
        }

        // 确保输出目录存在
        string outputFolder = string.IsNullOrEmpty(_config.filePath) 
            ? System.IO.Path.GetTempPath() 
            : _config.filePath;
        System.IO.Directory.CreateDirectory(outputFolder);

        // 开始遍历前全局抑制只读对话框
        int originalSuppressMode = 0;
        bool suppressEnabled = false;
        try
        {
            // 使用 UFSession.Ui (小写i) 获取 UI 接口
            var ui = ufSession.Ui;
            if (ui != null)
            {
                ui.AskSuppressDialogs(out originalSuppressMode);

                var flagField = typeof(UFConstants).GetField("UF_UI_SUPPRESS_READONLY_WARNING");
                int suppressFlag = 1;
                if (flagField != null)
                {
                    suppressFlag = (int)flagField.GetValue(null);
                }
                ui.SetSuppressDialogs(suppressFlag);
                suppressEnabled = true;
            }
        }
        catch { }

        // 开始深度优先遍历，初始层级为0
        try
        {
            TraverseAssembly(rootComponent, ufSession, theSession, 0, outputFolder);
        }
        finally
        {
            // 恢复原始对话框抑制模式
            if (suppressEnabled)
            {
                try
                {
                    ufSession.Ui.SetSuppressDialogs(originalSuppressMode);
                }
                catch { }
            }

            // 关闭所有事务处理器（如CSV文件等资源）
            foreach (var handler in _transactionHandlers)
            {
                handler.Finish();
            }

            // 任务完成后关闭窗口
            if (stopForm.InvokeRequired)
                stopForm.BeginInvoke(new Action(() => stopForm.Close()));
            else
                stopForm.Close();
        }
    }

    /// <summary>
    /// NX 卸载选项
    /// </summary>
    public static int GetUnloadOption(string arg)
    {
        return System.Convert.ToInt32(Session.LibraryUnloadOption.Immediately);
    }

    /// <summary>
    /// 深度优先递归遍历装配结构。
    /// </summary>
    private static void TraverseAssembly(Component comp, UFSession ufSession, Session theSession, int level, string outputFolder)
    {
        // ① 主动让UI线程处理消息，并检查是否已点击停止
        System.Windows.Forms.Application.DoEvents();
        if (StopForm.StopRequested) return;

        // 1. 按需加载当前组件（确保子件结构可见）
        try
        {
            Tag instanceTag = ufSession.Assem.AskInstOfPartOcc(comp.Tag);
            if (instanceTag != Tag.Null)
            {
                UFPart.LoadStatus loadStatus;
                ufSession.Assem.EnsureChildLoaded(instanceTag, out loadStatus);
            }
        }
        catch (Exception ex)
        {
            theSession.ListingWindow.WriteLine(string.Format(
                "{0}Warning: Could not ensure component '{1}' is loaded. Error: {2}",
                new string(' ', level * 2), comp.DisplayName, ex.Message));
            return;
        }

        // 2. 获取组件信息
        string compId = comp.DisplayName;
        string compName = AssemblyTraverserUtils.GetDescriptiveName(comp);
        Part currentPart = comp.Prototype as Part;

        // 3. 判断是否为装配体并获取子件（含原型二次确认）
        bool isAssembly = false;
        Component[] children = comp.GetChildren();
        if (children.Length > 0)
        {
            isAssembly = true;
        }
        else
        {
            if (currentPart != null && currentPart.ComponentAssembly != null)
            {
                Component protoRoot = currentPart.ComponentAssembly.RootComponent;
                if (protoRoot != null && protoRoot.GetChildren().Length > 0)
                {
                    isAssembly = true;
                    children = protoRoot.GetChildren();
                }
            }
        }

        // 4. 检查两个通配符列表是否有配置
        bool hasNameFilter = _config.namePatterns != null && _config.namePatterns.Length > 0;
        bool hasIdFilter = _config.idPatterns != null && _config.idPatterns.Length > 0;

        // 5. 名称匹配（如果有名称过滤）
        bool nameMatched = hasNameFilter && AssemblyTraverserUtils.IsMatching(compName, _config.namePatterns);

        // 6. ID匹配（如果有ID过滤）
        bool idMatched = hasIdFilter && AssemblyTraverserUtils.IsMatching(compId, _config.idPatterns);

        // 7. 决策标志（纯布尔逻辑）
        // 处理条件：名称或ID匹配，或者没有筛选条件且当前是叶子节点
        bool shouldProcess = nameMatched
                             || idMatched
                             || (!hasNameFilter && !hasIdFilter && !isAssembly);

        // 展开条件：不处理，但是装配体，小于最大处理层数
        bool shouldExpand = !shouldProcess && isAssembly && level < MaxLevel;

        // 8. 执行决策
        if (shouldProcess)
        {
            string matchType = (nameMatched ? "Name" : "") +
                               (idMatched ? (nameMatched ? "/ID" : "ID") : "");
            if (!hasNameFilter && !hasIdFilter && !isAssembly)
                matchType = "Default(all leaves)";

            theSession.ListingWindow.WriteLine(string.Format("{0}[Process - {1}] {2} (Name: {3}, ID: {4})",
                new string(' ', level * 2), matchType, compId, compName, compId));
            
            // 调用所有事务处理器，按顺序逐个执行
            foreach (var handler in _transactionHandlers)
            {
                handler.Process(comp, theSession, outputFolder, compId + "_" + compName);
            }
        }
        else if (shouldExpand)
        {
            theSession.ListingWindow.WriteLine(string.Format("{0}[Assembly] {1} (Name: {2}, ID: {3}) - expanding",
                new string(' ', level * 2), compId, compName, compId));
            foreach (Component child in children)
            {
                TraverseAssembly(child, ufSession, theSession, level + 1, outputFolder);
            }
        }
        else
        {
            // 不匹配的叶子节点，且有过滤限制
            theSession.ListingWindow.WriteLine(string.Format("{0}[Skip] {1} (Name: {2}, ID: {3})",
                new string(' ', level * 2), compId, compName, compId));
        }

        // 9. 统一关闭当前组件（根节点除外）
        if (level > 0 && currentPart != null)
        {
            try
            {
                currentPart.Close(BasePart.CloseWholeTree.False, BasePart.CloseModified.UseResponses, null);
            }
            catch (Exception ex)
            {
                theSession.ListingWindow.WriteLine(string.Format(
                    "    关闭组件时出错: {0} - {1}", comp.DisplayName, ex.Message));
            }
        }
    }
}
