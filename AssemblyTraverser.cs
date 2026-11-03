using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NXOpen;
using NXOpen.Assemblies;
using NXOpen.UF;

public sealed class AssemblyTraverser
{
    private static int _running;
    public static volatile bool StopRequested;
    private readonly TraversalConfig _config;
    private readonly List<ITransactionHandler> _handlers = new List<ITransactionHandler>();
    private readonly HashSet<string> _processed = new HashSet<string>(StringComparer.Ordinal);
    private NxPartResources _resources;
    private int _completed, _failed, _skipped;

    private AssemblyTraverser(TraversalConfig config)
    {
        _config = config;
        // Measure first: a screenshot error must not prevent measurements.
        if (config.flagXYZOn) _handlers.Add(new BoundingBoxExporter(config.coordinateSelection));
        if (config.flagImageOn) _handlers.Add(new ImageExporter(config.viewList));
    }

    public static void Run(TraversalConfig config)
    {
        if (config == null) throw new ArgumentNullException("config");
        config.Validate();
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new InvalidOperationException("已有遍历任务正在执行。");
        try
        {
            StopRequested = false;
            new AssemblyTraverser(config).Execute();
        }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    public static void RequestStop() { StopRequested = true; }

    private void Execute()
    {
        Session session = Session.GetSession();
        Part rootPart = session.Parts.Work;
        if (rootPart == null) throw new InvalidOperationException("请先打开装配并设置工作部件。");
        Component root = rootPart.ComponentAssembly.RootComponent;
        if (root == null) throw new InvalidOperationException("当前工作部件没有装配结构。");
        string output = Path.Combine(_config.filePath, "NX_Traverser_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") +
            "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(output);
        using (var log = new RunLog(session, output))
        {
            RunLog.Write("输出目录: " + output);
            RunLog.Write("每个匹配原型导出一次；匹配装配后不再单独导出其后代。坐标系=" + _config.coordinateSelection);
            RunLog.Write("逐节点释放临时加载部件；保留原有完整加载/已修改/活动部件。原有干净部分加载部件被本次使用后可卸载。");
            try
            {
                using (_resources = new NxPartResources(session))
                    Traverse(root, rootPart.ComponentAssembly, session, 0, output);
            }
            finally
            {
                foreach (ITransactionHandler handler in _handlers)
                {
                    try { handler.Finish(); }
                    catch (Exception ex) { ++_failed; RunLog.Error("Finish " + handler.GetType().Name, ex); }
                }
                try { UFSession.GetUFSession().Ui.SetStatus(string.Empty); }
                catch (Exception ex) { RunLog.Error("清理状态栏", ex); }
                RunLog.Write(string.Format("结束: 成功事务={0}, 失败={1}, 跳过={2}, 停止={3}",
                    _completed, _failed, _skipped, StopRequested));
            }
        }
    }

    private void Traverse(Component comp, ComponentAssembly assembly, Session session, int level, string output)
    {
        if (StopRequested || comp == null || comp.Tag == Tag.Null) return;
        Tag componentTag = comp.Tag;
        Tag pinnedPart = Tag.Null;
        try
        {
            if (comp.IsSuppressed) { ++_skipped; return; }
            Part part = NxGeometry.EnsurePrototype(comp, assembly, false);
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
                bool wasModified = part.IsModified;
                try
                {
                    using (var context = new NxPartContext(session, part))
                    {
                        foreach (ITransactionHandler handler in _handlers)
                        {
                            if (StopRequested) break;
                            string stage = handler.GetType().Name + " " + id;
                            try
                            {
                                RunLog.Write("[Begin] " + stage);
                                handler.Process(comp, session, output, id + "_" + name);
                                ++_completed;
                            }
                            catch (Exception ex) { ++_failed; RunLog.Error(stage, ex); }
                        }
                    }
                }
                finally
                {
                    if (!wasModified && part.IsModified)
                        RunLog.Write("[WARN] 部件被 NX 标记为已修改: " + id +
                            "；本工具未保存或清除修改标记，请本地核查加载、显示切换及站点回调。");
                }
            }
            else if (children.Length > 0 && level < _config.maxDepth)
            {
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
            ++_failed;
            RunLog.Error("Traverse level=" + level + " tag=" + componentTag, ex);
        }
        finally
        {
            if (pinnedPart != Tag.Null) _resources.Unpin(pinnedPart);
            // Runs after context restoration, including failed loads/exports.
            if (!_resources.ReleaseUnused("level=" + level + " component=" + componentTag)) ++_failed;
        }
    }
}
