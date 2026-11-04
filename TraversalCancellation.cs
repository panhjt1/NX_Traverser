using System;
using NXOpen.UF;

// All UF calls stay on the NX thread. The stop window only sets the persistent
// managed flag; NX's transient abort flag may already be cleared by an API.
internal sealed class TraversalCancellation : IDisposable
{
    private static TraversalCancellation _current;
    private bool _reported, _nativeFaulted;

    public TraversalCancellation()
    {
        if (_current != null) throw new InvalidOperationException("取消监控已在运行。");
        _current = this;
    }

    private bool PollNativeAbort()
    {
        if (_nativeFaulted) return false;
        try
        {
            bool aborted;
            UFSession.GetUFSession().Abort.AskFlagStatus(out aborted);
            if (!aborted) return false;
            // Latch BEFORE clearing NX's signal so later nodes cannot resume.
            AssemblyTraverser.RequestStop();
            Report("NX 取消请求");
            UFSession.GetUFSession().Abort.ClearAbort();
            return true;
        }
        catch (Exception ex)
        {
            _nativeFaulted = true;
            AssemblyTraverser.RequestStop();
            RunLog.Error("取消监控失败，停止继续遍历", ex);
            throw new InvalidOperationException("无法读取或处理 NX 取消标记，已停止后续处理。", ex);
        }
    }

    private void Report(string reason)
    {
        if (_reported) return;
        _reported = true;
        RunLog.Write("[STOP requested] " + reason + "；停止后续遍历，恢复显示状态并释放临时部件。");
    }

    public static void Checkpoint()
    {
        if (_current != null) _current.PollNativeAbort();
        if (!AssemblyTraverser.StopRequested) return;
        if (_current != null) _current.Report("用户或程序请求停止");
        throw new OperationCanceledException("遍历已停止，不再处理后续事务和组件。");
    }

    public static bool IsCancellation(Exception error)
    {
        if (error is OperationCanceledException)
        {
            AssemblyTraverser.RequestStop();
            if (_current != null)
            {
                _current.Report("操作已取消");
                _current.PollNativeAbort(); // A pending native signal must not leak into restoration.
            }
            return true;
        }
        // Also inspect abort on an NX API exception, before the outer loop can
        // mistake it for an ordinary per-component failure and move on.
        return _current != null && _current.PollNativeAbort();
    }

    public void Dispose()
    {
        try { PollNativeAbort(); }
        finally { _current = null; }
    }
}
