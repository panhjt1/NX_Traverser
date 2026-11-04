using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NXOpen;
using NXOpen.Assemblies;
using NXOpen.UF;

public sealed class AssemblyTraverser
{
    private sealed class TransactionTotals
    {
        public int Succeeded, Failed, Canceled;
        public bool FinishFailed;
    }
    private static int _running;
    public static volatile bool StopRequested;
    private readonly TraversalConfig _config;
    private readonly List<ITransactionHandler> _handlers;
    private readonly List<TransactionTotals> _totals = new List<TransactionTotals>();
    private readonly List<string> _failureSummary = new List<string>();
    private const int FailureSummaryLimit = 10;
    private bool _cleanupFailureReported;
    private readonly HashSet<string> _processed = new HashSet<string>(StringComparer.Ordinal);
    private NxPartResources _resources;
    private int _completed, _failed, _skipped;

    private AssemblyTraverser(TraversalConfig config, List<ITransactionHandler> handlers)
    {
        _config = config;
        _handlers = handlers;
        foreach (var handler in handlers) _totals.Add(new TransactionTotals());
    }

    // Default composition only. No transaction-specific decisions in traversal.
    private static List<ITransactionHandler> BuildTransactionHandlers(TraversalConfig config)
    {
        var handlers = new List<ITransactionHandler>();
        if (config.flagXYZOn) handlers.Add(new BoundingBoxExporter(config.coordinateSelection));
        if (config.flagImageOn) handlers.Add(new ImageExporter(config.viewList, config.useIsolatedImageView));
        return handlers;
    }

    public static void Run(TraversalConfig config)
    {
        if (config == null) throw new ArgumentNullException("config");
        config.Validate();
        RunCore(config, BuildTransactionHandlers(config));
    }

    // Explicit composition supports new transactions without changing traversal.
    // The driver owns Finish after accepting this list; pass fresh instances.
    public static void Run(TraversalConfig config, IEnumerable<ITransactionHandler> transactions)
    {
        if (config == null) throw new ArgumentNullException("config");
        config.Validate(false);
        if (transactions == null) throw new ArgumentNullException("transactions");
        var handlers = new List<ITransactionHandler>(transactions);
        if (handlers.Count == 0 || handlers.Exists(handler => handler == null))
            throw new ArgumentException("必须提供至少一个非空事务处理器。", "transactions");
        for (int i = 0; i < handlers.Count; ++i)
            for (int j = 0; j < i; ++j)
                if (ReferenceEquals(handlers[i], handlers[j]))
                    throw new ArgumentException("同一事务实例不能重复注册。", "transactions");
        RunCore(config, handlers);
    }

    private static void RunCore(TraversalConfig config, List<ITransactionHandler> handlers)
    {
        if (TraversalStopWindow.HasLiveWindow)
            throw new InvalidOperationException("上一次停止窗口尚未关闭，不能启动新的遍历。");
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new InvalidOperationException("已有遍历任务正在执行。");
        try
        {
            StopRequested = false;
            new AssemblyTraverser(config, handlers).Execute();
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private void FinishHandlers()
    {
        var errors = new List<KeyValuePair<string, Exception>>();
        var records = new List<string>();
        for (int i = 0; i < _handlers.Count; ++i)
        {
            ITransactionHandler handler = _handlers[i];
            var timer = Stopwatch.StartNew();
            try { handler.Finish(); }
            catch (Exception ex)
            {
                ++_failed;
                _totals[i].FinishFailed = true;
                RememberFailure("事务 " + (i + 1) + " " + handler.GetType().Name + " 收尾", ex.Message);
                errors.Add(new KeyValuePair<string, Exception>("Finish " + handler.GetType().Name, ex));
            }
            records.Add("[Finish result] #" + (i + 1) + " " + handler.GetType().Name +
                " Success=" + !_totals[i].FinishFailed + " ElapsedMs=" + timer.ElapsedMilliseconds);
        }
        // Even a log I/O failure must not prevent another handler's cleanup.
        foreach (string record in records) RunLog.Write(record);
        foreach (var error in errors) RunLog.Error(error.Key, error.Value);
    }

    public static void RequestStop() { StopRequested = true; }

    private void Execute()
    {
        Session session = null;
        RunLog log = null;
        var timer = Stopwatch.StartNew();
        try
        {
            session = Session.GetSession();
            Part rootPart = session.Parts.Work;
            if (rootPart == null) throw new InvalidOperationException("请先打开装配并设置工作部件。");
            if (session.Parts.Display != rootPart)
                throw new InvalidOperationException("装配内导出要求原装配同时为工作部件和显示部件，请先在 NX 中设置。");
            Component root = rootPart.ComponentAssembly.RootComponent;
            if (root == null) throw new InvalidOperationException("当前工作部件没有装配结构。");
            string output = ExportUtilities.CreateRunDirectory(_config.filePath, DateTime.Now);
            log = new RunLog(session, output);
            RunLog.Write("输出目录: " + output);
            RunLog.WriteBuildIdentity();
            RunLog.Write("[Configuration] MaxDepth=" + _config.maxDepth + " CoordinateMode=" + _config.coordinateSelection +
                " NamePatterns=" + string.Join(" | ", _config.namePatterns) + " IdPatterns=" + string.Join(" | ", _config.idPatterns));
            RunLog.Write("[Root] " + rootPart.FullPath + " Units=" + rootPart.PartUnits +
                " State=" + rootPart.PartLoadState + " ReadOnly=" + rootPart.IsReadOnly + " Modified=" + rootPart.IsModified);
            RunLog.Write("每个匹配原型导出一次；匹配装配后不再单独导出其后代。");
            RunLog.Write("逐节点释放临时加载部件；保留原有完整加载/已修改/活动部件。原有干净部分加载部件被本次使用后可卸载。");
            RunLog.Write("事务顺序: " + string.Join(" -> ", _handlers.ConvertAll(h => h.GetType().Name)));
            using (var cancellation = new TraversalCancellation())
            using (var stopWindow = new TraversalStopWindow())
            using (_resources = new NxPartResources(session))
            {
                RunLog.Write("可点击独立窗口的“停止后续遍历”；当前 NX 调用返回后停止，恢复和卸载仍会执行。");
                Traverse(root, rootPart.ComponentAssembly, session, 0, output);
            }
        }
        catch (Exception ex)
        {
            HandleFailure("运行初始化或收尾", ex);
            throw;
        }
        finally
        {
            try
            {
                // Single finalization site, including preflight/output failures.
                FinishHandlers();
                if (_resources != null && _resources.CleanupFailed && !_cleanupFailureReported)
                    RecordCleanupFailure("最终部件释放");
                if (session != null)
                {
                    try { UFSession.GetUFSession().Ui.SetStatus(string.Empty); }
                    catch (Exception ex) { RunLog.Error("清理状态栏", ex); }
                }
                RunLog.Write(string.Format("结束: 成功事务={0}, 失败={1}, 跳过={2}, 停止={3}",
                    _completed, _failed, _skipped, StopRequested));
                if (log != null) ShowSummary(log, timer.Elapsed);
            }
            finally { if (log != null) log.Dispose(); }
        }
    }

    private bool HandleFailure(string stage, Exception error)
    {
        if (TraversalCancellation.IsCancellation(error))
        {
            RunLog.Write("[CANCELED] " + stage + ": " + error.Message);
            return true;
        }
        ++_failed;
        RememberFailure(stage, error.Message);
        RunLog.Error(stage, error);
        return false;
    }

    private void RememberFailure(string stage, string message)
    {
        // Bounded strings only: do not retain NX objects or a large assembly's
        // complete result list in memory merely to display a final report.
        if (_failureSummary.Count >= FailureSummaryLimit) return;
        string line = (stage + ": " + message).Replace('\r', ' ').Replace('\n', ' ');
        _failureSummary.Add(line.Length <= 240 ? line : line.Substring(0, 240) + "…");
    }

    private void RecordCleanupFailure(string stage)
    {
        ++_failed;
        _cleanupFailureReported = true;
        RememberFailure(stage, "部件释放失败，已停止；详情见日志。");
    }

    private void ShowSummary(RunLog log, TimeSpan elapsed)
    {
        var lines = new List<string> { "========== NX Traverser 执行结果 ==========",
            "结果: " + (StopRequested ? "已停止" : _failed > 0 ? "完成（有失败）" : "完成") };
        for (int i = 0; i < _handlers.Count; ++i)
        {
            ITransactionHandler handler = _handlers[i];
            TransactionTotals total = _totals[i];
            string name = handler is ImageExporter ? "截图" : handler is BoundingBoxExporter ? "XYZ 测量" : handler.GetType().Name;
            string outcome = total.Failed > 0 || total.FinishFailed ? "有失败" : total.Canceled > 0 ? "已取消" :
                total.Succeeded > 0 ? "成功" : "未执行";
            lines.Add(string.Format("【{0}】事务 {1} {2}: 成功={3}, 失败={4}, 取消={5}, 收尾={6}",
                outcome, i + 1, name, total.Succeeded, total.Failed, total.Canceled, total.FinishFailed ? "失败" : "成功"));
        }
        lines.Add(string.Format("总计: 成功事务={0}, 失败={1}, 跳过节点={2}, 停止={3}, 耗时={4:F1}秒",
            _completed, _failed, _skipped, StopRequested, elapsed.TotalSeconds));
        if (_failureSummary.Count > 0)
        {
            lines.Add("失败摘要（最多 " + FailureSummaryLimit + " 条，完整明细见日志）:");
            foreach (string failure in _failureSummary) lines.Add("  " + failure);
        }
        lines.Add("输出目录: " + Path.GetDirectoryName(log.FilePath));
        lines.Add("详细日志: " + log.FilePath);
        log.ShowSummary(lines);
    }

    private void ProcessTarget(Component comp, Part part, Session session, string output, string id, string name)
    {
        bool wasModified = part.IsModified;
        var context = new TransactionContext(session, comp, part, output, id, name);
        try
        {
            for (int i = 0; i < _handlers.Count; ++i)
            {
                if (StopRequested) break;
                ITransactionHandler handler = _handlers[i];
                string stage = handler.GetType().Name + " " + id;
                string outcome = "Failed";
                var timer = Stopwatch.StartNew();
                try
                {
                    RunLog.Write("[Begin] " + stage);
                    context.Execute(handler);
                    ++_completed;
                    ++_totals[i].Succeeded;
                    outcome = "Succeeded";
                }
                catch (Exception ex)
                {
                    if (HandleFailure(stage, ex)) { ++_totals[i].Canceled; outcome = "Canceled"; }
                    else ++_totals[i].Failed;
                }
                finally
                {
                    RunLog.Write("[Transaction end] #" + (i + 1) + " " + stage + " Result=" + outcome +
                        " ElapsedMs=" + timer.ElapsedMilliseconds);
                }
            }
        }
        finally
        {
            try { context.Close(); }
            finally
            {
                if (!wasModified && part.IsModified)
                    RunLog.Write("[WARN] 部件被 NX 标记为已修改: " + id +
                        "；本工具未保存或清除修改标记，请本地核查加载及站点回调。");
            }
        }
    }

    private void Traverse(Component comp, ComponentAssembly assembly, Session session, int level, string output)
    {
        if (StopRequested || comp == null || comp.Tag == Tag.Null) return;
        Tag componentTag = comp.Tag;
        Tag pinnedPart = Tag.Null;
        var timer = Stopwatch.StartNew();
        try
        {
            TraversalCancellation.Checkpoint();
            RunLog.Write("[Traverse begin] level=" + level + " tag=" + componentTag);
            if (comp.IsSuppressed) { ++_skipped; RunLog.Write("[Skip suppressed] tag=" + componentTag); return; }
            Part part = NxGeometry.EnsurePrototype(comp, assembly);
            _resources.Pin(part);
            pinnedPart = part.Tag;
            Component[] children = comp.GetChildren();
            string id = comp.DisplayName;
            string name = AssemblyTraverserUtils.GetDescriptiveName(comp);
            bool filtered = _config.namePatterns.Length > 0 || _config.idPatterns.Length > 0;
            bool matched = filtered
                ? AssemblyTraverserUtils.IsMatching(name, _config.namePatterns) || AssemblyTraverserUtils.IsMatching(id, _config.idPatterns)
                : children.Length == 0;
            if (matched)
            {
                // Part tags change after unload/reload; the managed/native path
                // identifies the prototype across different occurrences.
                string key = string.IsNullOrEmpty(part.FullPath) ? part.Name : part.FullPath;
                if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("无法取得稳定的原型标识。");
                if (!_processed.Add(key))
                {
                    ++_skipped;
                    RunLog.Write("[Skip duplicate prototype] " + id);
                    return;
                }
                RunLog.Write(string.Format("[Process] {0} Name={1} ReadOnly={2} Modified={3}",
                    id, name, part.IsReadOnly, part.IsModified));
                UFSession.GetUFSession().Ui.SetStatus("正在处理: " + id);
                ProcessTarget(comp, part, session, output, id, name);
            }
            else if (children.Length > 0 && level < _config.maxDepth)
            {
                RunLog.Write("[Expand] " + id + " Name=" + name + " Children=" + children.Length + " level=" + level);
                foreach (Component child in children)
                {
                    if (StopRequested) break;
                    Traverse(child, assembly, session, level + 1, output);
                }
            }
            else
            {
                ++_skipped;
                RunLog.Write("[Skip] " + id + (children.Length > 0 ? " 已到最大层级" : " 不匹配"));
            }
        }
        catch (Exception ex)
        {
            HandleFailure("Traverse level=" + level + " tag=" + componentTag, ex);
        }
        finally
        {
            if (pinnedPart != Tag.Null) _resources.Unpin(pinnedPart);
            // Runs after context restoration, including failed loads/exports.
            if (!_resources.ReleaseUnused("level=" + level + " component=" + componentTag))
                RecordCleanupFailure("部件释放 level=" + level + " component=" + componentTag);
            RunLog.Write("[Traverse end] level=" + level + " tag=" + componentTag + " ElapsedMs=" + timer.ElapsedMilliseconds);
        }
    }
}
