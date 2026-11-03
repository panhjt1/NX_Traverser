using System;
using NXOpen;
using NXOpen.Assemblies;
using NXOpen.UF;

// Uses the displayed assembly's temporary isolate view. No source prototype is
// made the work/display part. Start from a normal, single modeling viewport.
internal sealed class NxImageContext : IDisposable
{
    private readonly Session _session;
    private readonly Part _display;
    private readonly Part _work;
    private readonly Layout _layout;
    private readonly NxViewState _original;
    private readonly bool _expanded;
    private bool _createAttempted;
    private bool _disposed;
    private Tag _isolateTag;
    public View View { get; private set; }
    public double[,] Transform { get; private set; }

    public NxImageContext(Session session, Component component)
    {
        _session = session;
        _display = session.Parts.Display;
        _work = session.Parts.Work;
        if (_display == null || _work != _display || component.OwningPart != _display)
        {
            AssemblyTraverser.RequestStop();
            throw new InvalidOperationException("隔离截图要求遍历工作装配同时是显示部件，请先激活顶层装配。");
        }
        _layout = _display.Layouts.Current;
        View[] active = _display.Views.GetActiveViews();
        View original = _display.Views.WorkView;
        if (_layout == null || active.Length != 1 || active[0].Tag != original.Tag ||
            !(original is ModelingView) || _layout.GetViews().Length != 1)
        {
            AssemblyTraverser.RequestStop();
            throw new InvalidOperationException("隔离截图目前要求普通单建模视口；请先退出隔离/制图/分屏视图。");
        }
        Tag explosion;
        UFSession.GetUFSession().Assem.AskViewExplosion(original.Tag, out explosion);
        if (explosion != Tag.Null)
            throw new InvalidOperationException("请先退出爆炸视图，以保证截图方向与原型一致。");
        _original = new NxViewState(original);
        _expanded = _display.Views.IsWorkViewExpanded();
        LogViewState("captured");
        View = original;
        Transform = new double[,] { { 1, 0, 0, 0 }, { 0, 1, 0, 0 }, { 0, 0, 1, 0 }, { 0, 0, 0, 1 } };
        try
        {
            if (component.Parent == null) return; // The whole displayed root.
            UFSession.GetUFSession().Assem.AskTransformOfOcc(component.Tag, Transform);
            // Validate before mutating the view (reject reflection/shear).
            ExportUtilities.PrototypeViewInAssembly(new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } }, Transform);
            NxPartResources.BeforeLoad(component.Prototype as BasePart);
            _createAttempted = true;
            RunLog.NxCall("CreateIsolateView " + component.DisplayName, () =>
            {
                View isolated;
                using (ErrorList errors = _display.ComponentAssembly.CreateIsolateViewWithComponents(new[] { component }, out isolated))
                {
                    if (isolated != null) { View = isolated; _isolateTag = isolated.Tag; }
                    if (errors != null && errors.Length > 0)
                    {
                        for (int i = 0; i < errors.Length; ++i)
                        {
                            ErrorInfo error = errors.GetErrorInfo(i);
                            RunLog.Write("[Isolate error] code=" + error.ErrorCode + " " + error.Description);
                        }
                        throw new InvalidOperationException("创建临时隔离视图失败，未回退到只读原型激活。");
                    }
                }
                if (_isolateTag == Tag.Null || _isolateTag == _original.Tag)
                    throw new InvalidOperationException("NX 未创建独立的临时隔离视图，请先退出已有隔离视图。");
                View.MakeWork();
                bool visible = false;
                foreach (View view in _display.Views.GetActiveViews()) if (view.Tag == _isolateTag) visible = true;
                if (!visible || _display.Views.WorkView.Tag != _isolateTag)
                    throw new InvalidOperationException("临时隔离视图未成为活动工作视图，已拒绝截图。");
                EnsurePartContext();
                LogViewState("isolated");
            });
        }
        catch { Dispose(); throw; }
    }

    private void EnsurePartContext()
    {
        if (_session.Parts.Display != _display || _session.Parts.Work != _work)
            throw new InvalidOperationException("隔离截图期间工作/显示部件意外变化。");
    }

    private void LogViewState(string stage)
    {
        try
        {
            Layout current = _display.Layouts.Current;
            View work = _display.Views.WorkView;
            View[] active = _display.Views.GetActiveViews();
            var tags = new string[active.Length];
            for (int i = 0; i < active.Length; ++i) tags[i] = active[i].Tag.ToString();
            RunLog.Write("[Image state] " + stage + " OriginalLayout=" + _layout.Tag +
                " CurrentLayout=" + (current == null ? Tag.Null : current.Tag) +
                " OriginalLayoutDisplayStatus=" + _layout.DisplayStatus +
                " OriginalView=" + _original.Tag + " WorkView=" + (work == null ? Tag.Null : work.Tag) +
                " ActiveViews=" + string.Join(",", tags) + " IsolateView=" + _isolateTag);
        }
        catch (Exception ex) { RunLog.Error("读取截图视图诊断 " + stage, ex); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            EnsurePartContext();
            LogViewState("before restore");
            if (_createAttempted)
            {
                RunLog.NxCall("Restore image layout", () =>
                {
                    // Current can change while Open still regards the original
                    // layout as displayed (NX 925052). Select the saved layout
                    // with ChangeLayout; never reopen it with Layout.Open.
                    Layout current = _display.Layouts.Current;
                    if (current == null || current.Tag != _layout.Tag)
                    {
                        RunLog.NxCall("ChangeLayout (restore image)", () => _display.Layouts.ChangeLayout(_layout));
                        LogViewState("after ChangeLayout");
                        Layout selected = _display.Layouts.Current;
                        if (selected == null || selected.Tag != _layout.Tag)
                            throw new InvalidOperationException("ChangeLayout 未恢复原布局，已停止。");
                    }
                    else RunLog.Write("[Image restore] 已是原布局，直接恢复视图。");
                    foreach (View active in _display.Views.GetActiveViews())
                    {
                        if (active.Tag != _isolateTag || active.Tag == _original.Tag) continue;
                        var isolated = active as ModelingView;
                        var original = _original.View as ModelingView;
                        if (isolated == null || original == null)
                            throw new InvalidOperationException("临时视图仍在显示，无法用布局替换恢复；已停止。");
                        RunLog.NxCall("Replace isolated view", () => _layout.ReplaceView(isolated, original, false));
                    }
                    _original.View.MakeWork();
                    if (_expanded) _original.View.Expand();
                    else if (_display.Views.IsWorkViewExpanded()) _display.Views.UnexpandWork();
                });
            }
            _original.Restore();
            _session.DisplayManager.MakeUpToDate();
            LogViewState("after restore");
            Layout restoredLayout = _display.Layouts.Current;
            View[] restored = _display.Views.GetActiveViews();
            if (restoredLayout == null || restoredLayout.Tag != _layout.Tag || restored.Length != 1 ||
                restored[0].Tag != _original.Tag || _display.Views.WorkView.Tag != _original.Tag ||
                _display.Views.IsWorkViewExpanded() != _expanded)
                throw new InvalidOperationException("原布局/工作视图未完整恢复，已停止。");
            RunLog.Write("[Image context restored] 原布局、视图、WCS/三重轴可见性已恢复。");
        }
        catch (Exception ex)
        {
            AssemblyTraverser.RequestStop();
            // A failed temporary view may still reference any loaded child.
            // Preserve this bounded working set and stop before another load.
            NxPartResources.KeepCurrentPartsOpen();
            RunLog.Error("恢复截图视图失败", ex);
            throw;
        }
    }
}

// View-local flags restore independently even if restoring the transform fails.
internal sealed class NxViewState
{
    public readonly View View;
    public readonly Tag Tag;
    private readonly Matrix3x3 _rotation;
    private readonly Point3d _origin;
    private readonly double _scale;
    private readonly bool _wcs, _triad;

    public NxViewState(View view)
    {
        View = view; Tag = view.Tag;
        _rotation = view.Matrix; _origin = view.Origin; _scale = view.Scale;
        _wcs = view.WcsVisibility; _triad = view.TriadVisibility;
    }

    public void Restore()
    {
        try
        {
            // The original assembly view usually stays untouched in isolate
            // mode. Do not write it back unnecessarily and dirty its part.
            Matrix3x3 m = View.Matrix;
            Point3d p = View.Origin;
            if (m.Xx != _rotation.Xx || m.Xy != _rotation.Xy || m.Xz != _rotation.Xz ||
                m.Yx != _rotation.Yx || m.Yy != _rotation.Yy || m.Yz != _rotation.Yz ||
                m.Zx != _rotation.Zx || m.Zy != _rotation.Zy || m.Zz != _rotation.Zz ||
                p.X != _origin.X || p.Y != _origin.Y || p.Z != _origin.Z || View.Scale != _scale)
                View.SetRotationTranslationScale(_rotation, _origin, _scale);
        }
        finally
        {
            try { if (View.WcsVisibility != _wcs) View.WcsVisibility = _wcs; }
            finally { if (View.TriadVisibility != _triad) View.TriadVisibility = _triad; }
        }
    }
}
