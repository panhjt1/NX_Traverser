using System;
using NXOpen;
using NXOpen.Assemblies;

// Exact prototype geometry queries must run in that part's context (Siemens
// UF_MODL_ask_bounding_box_exact). Preserve the original work occurrence/refset.
internal sealed class NxPartContext : IDisposable
{
    private readonly Session _session;
    private readonly Part _display;
    private readonly Part _work;
    private readonly Component _workComponent;
    private bool _disposed;

    public NxPartContext(Session session, Part part)
    {
        _session = session;
        _display = session.Parts.Display;
        _work = session.Parts.Work;
        _workComponent = session.Parts.WorkComponent;
        try
        {
            if (session.Parts.Display != part || session.Parts.Work != part)
                SetDisplay(part);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void SetDisplay(Part part)
    {
        RunLog.Write("[Context] SetActiveDisplay: " + part.Name);
        PartLoadStatus loadStatus;
        PartCollection.SdpsStatus result = _session.Parts.SetActiveDisplay(part,
            DisplayPartOption.ReplaceExisting, PartDisplayPartWorkPartOption.SameAsDisplay, out loadStatus);
        using (loadStatus) { CheckLoadStatus(loadStatus, "SetActiveDisplay"); }
        if (result != PartCollection.SdpsStatus.Ok)
            throw new InvalidOperationException("不能切换到建模显示部件: " + result);
    }

    public static void CheckLoadStatus(PartLoadStatus status, string stage)
    {
        if (status == null || status.NumberUnloadedParts == 0) return;
        for (int i = 0; i < status.NumberUnloadedParts; ++i)
            RunLog.Write("[Load] " + stage + ": " + status.GetPartName(i) + " code=" +
                status.GetStatus(i) + " " + status.GetStatusDescription(i));
        throw new InvalidOperationException(stage + " 未能完整加载部件，已跳过，详情见日志。");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_display != null && _session.Parts.Display != _display) SetDisplay(_display);
            if (_workComponent != null && _session.Parts.WorkComponent != _workComponent)
            {
                PartLoadStatus status;
                _session.Parts.SetWorkComponent(_workComponent, PartCollection.RefsetOption.PreserveRefset,
                    PartCollection.WorkComponentOption.Given, out status);
                using (status) { CheckLoadStatus(status, "RestoreWorkComponent"); }
            }
            else if (_work != null && _session.Parts.Work != _work)
                _session.Parts.SetWork(_work);
        }
        catch (Exception ex)
        {
            // Do not continue processing with the wrong assembly/coordinate context.
            AssemblyTraverser.RequestStop();
            RunLog.Error("恢复显示/工作部件失败；已停止遍历", ex);
            throw;
        }
    }
}
