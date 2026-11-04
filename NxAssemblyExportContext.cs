using System;
using System.Collections.Generic;
using NXOpen;
using NXOpen.Assemblies;
using NXOpen.UF;

// Temporary occurrence visibility in the EXISTING assembly/window. No isolate
// view, layout change, work-part switch, reference-set edit or source WCS write.
public sealed class NxAssemblyExportContext : IDisposable
{
    private sealed class Visibility
    {
        public DisplayableObject Object;
        public bool Blanked;
    }
    private readonly Session _session;
    private readonly Part _assembly;
    private readonly Component _workComponent, _target, _root;
    private readonly List<Component> _path = new List<Component>();
    private readonly Dictionary<Tag, Visibility> _changed = new Dictionary<Tag, Visibility>();
    private readonly NxViewState _state;
    private bool _disposed;
    public Part AssemblyPart { get { return _assembly; } }
    public Component Target { get { return _target; } }
    public Part TargetPart { get { return _target.Prototype as Part; } }
    public readonly View View;
    public readonly double[,] Transform = new double[4, 4];

    public NxAssemblyExportContext(Session session, Component target)
    {
        TraversalCancellation.Checkpoint();
        _session = session; _target = target;
        _assembly = session.Parts.Display;
        _workComponent = session.Parts.WorkComponent;
        if (_assembly == null || session.Parts.Work != _assembly)
            throw new InvalidOperationException("装配内导出要求原装配同时为工作/显示部件。");
        _root = _assembly.ComponentAssembly.RootComponent;
        View = _assembly.ModelingViews.WorkView;
        View[] active = _assembly.Views.GetActiveViews();
        if (!(View is ModelingView) || active.Length != 1 || active[0].Tag != View.Tag)
            throw new InvalidOperationException("装配内导出要求普通单建模视口，请先退出制图/分屏/隔离窗口。");
        Tag explosion;
        UFSession.GetUFSession().Assem.AskViewExplosion(View.Tag, out explosion);
        if (explosion != Tag.Null)
            throw new InvalidOperationException("请先退出爆炸视图，再按部件自身 ACS 截图。");
        var seen = new HashSet<Tag>();
        for (Component c = target; c != null; c = c.Parent)
        {
            if (!seen.Add(c.Tag) || _path.Count > 100) throw new InvalidOperationException("组件路径循环或过深。");
            _path.Add(c);
            if (_root != null && c.Tag == _root.Tag) break;
        }
        if (_root == null || _path.Count == 0 || _path[_path.Count - 1].Tag != _root.Tag)
            throw new InvalidOperationException("目标实例不在当前显示装配中。");
        _path.Reverse();
        if (target.Tag == _root.Tag)
            for (int i = 0; i < 4; ++i) Transform[i, i] = 1;
        else UFSession.GetUFSession().Assem.AskTransformOfOcc(target.Tag, Transform);
        // Validate rotation before changing visibility, including mirrored data.
        ExportUtilities.PrototypeViewInAssembly(new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } }, Transform);
        _state = new NxViewState(View);
        try
        {
            var siblings = new List<DisplayableObject>();
            var pathToShow = new List<DisplayableObject>();
            for (int i = 0; i + 1 < _path.Count; ++i)
            {
                foreach (Component child in _path[i].GetChildren())
                    if (!child.IsSuppressed && child.Tag != _path[i + 1].Tag) siblings.Add(child);
                pathToShow.Add(_path[i + 1]);
            }
            ChangeVisibility(siblings, true, "Hide other branches");
            ChangeVisibility(pathToShow, false, "Show target path");
            RunLog.NxCall("Prepare target display " + target.DisplayName,
                () => NxGeometry.PrepareDisplay(target, _assembly.ComponentAssembly));
            _session.DisplayManager.MakeUpToDate();
            AssertContext();
            RunLog.Write("[Export context] 原装配内仅显示目标，截图/测量共用: " + target.DisplayName);
        }
        catch { Dispose(); throw; }
    }

    private void ChangeVisibility(IEnumerable<DisplayableObject> objects, bool blank, string stage)
    {
        var changes = new List<DisplayableObject>();
        var seen = new HashSet<Tag>();
        foreach (DisplayableObject obj in objects)
        {
            if (obj == null || !seen.Add(obj.Tag) || obj.IsBlanked == blank) continue;
            // Record BEFORE calling NX: a failed batch may have partially applied.
            if (!_changed.ContainsKey(obj.Tag))
                _changed.Add(obj.Tag, new Visibility { Object = obj, Blanked = obj.IsBlanked });
            changes.Add(obj);
        }
        ApplyVisibility(changes, blank, stage);
    }

    private void ApplyVisibility(List<DisplayableObject> objects, bool blank, string stage)
    {
        if (objects.Count == 0) return;
        NxPartResources.BeforeLoad(null); // Show can implicitly load display data.
        RunLog.NxCall(stage + " Count=" + objects.Count, () =>
        {
            if (blank) _session.DisplayManager.BlankObjects(objects.ToArray());
            else _session.DisplayManager.UnblankObjects(objects.ToArray());
            foreach (DisplayableObject obj in objects)
                if (obj.IsBlanked != blank) throw new InvalidOperationException("NX 未完成显示状态更改: tag=" + obj.Tag);
        }, cancelable: !_disposed);
        RunLog.Write("[Image visibility] " + stage + " Changed=" + objects.Count +
            " LoadedParts=" + _session.Parts.ToArray().Length);
    }

    private bool InTarget(DisplayableObject obj)
    {
        if (_target.Tag == _root.Tag) return true;
        Component owner = obj as Component ?? obj.OwningComponent;
        int depth = 0;
        for (Component c = owner; c != null && depth++ <= 100; c = c.Parent)
            if (c.Tag == _target.Tag) return true;
        return false;
    }

    private bool IsPathComponent(DisplayableObject obj)
    {
        if (!(obj is Component)) return false;
        foreach (Component c in _path) if (c.Tag == obj.Tag) return true;
        return false;
    }

    internal static DisplayableObject WholeObject(DisplayableObject obj)
    {
        var face = obj as Face;
        if (face != null) return face.GetBody();
        var edge = obj as Edge;
        return edge != null ? edge.GetBody() : obj;
    }

    // A component-only mask does not hide an ancestor's own bodies/curves/PMI.
    // Fit FIRST so off-screen objects cannot leak in when the camera changes.
    // Then mask auxiliary/foreign visible objects in assembly occurrence space, never by
    // changing the blank status of their prototype objects.
    public void PrepareFrame()
    {
        AssertContext();
        View.Fit();
        _session.DisplayManager.MakeUpToDate();
        var foreign = new List<DisplayableObject>();
        foreach (DisplayableObject obj in View.AskVisibleObjects())
        {
            DisplayableObject whole = WholeObject(obj);
            bool keepComponent = whole is Component && (InTarget(whole) || IsPathComponent(whole));
            bool keepGeometry = InTarget(whole) && IsModelGeometry(whole);
            if (!keepComponent && !keepGeometry)
            {
                if (!(whole is Component) && whole.OwningComponent == null && whole.OwningPart != _assembly)
                    throw new InvalidOperationException("不能在原装配中确认对象归属，拒绝修改源原型显示状态。");
                foreign.Add(whole);
            }
        }
        ChangeVisibility(foreign, true, "Hide auxiliary and other geometry");
        View.FitToObjects(new IFitTo[] { _target });
        _session.DisplayManager.MakeUpToDate();
        bool hasTarget = false;
        foreach (DisplayableObject obj in View.AskVisibleObjects())
        {
            DisplayableObject whole = WholeObject(obj);
            if (InTarget(whole) && IsModelGeometry(whole)) hasTarget = true;
            else if (!(whole is Component) || (!InTarget(whole) && !IsPathComponent(whole)))
                throw new InvalidOperationException("画面仍含目标以外的对象，停止本次截图: tag=" + whole.Tag);
        }
        if (!hasTarget) throw new InvalidOperationException("目标没有可见显示对象，请检查参考集、图层及隐藏状态。");
        AssertContext();
    }

    private static bool IsModelGeometry(DisplayableObject obj)
    {
        return obj is Body || obj is NXOpen.Facet.FacetedBody;
    }

    // Use occurrence members, not AskVisibleObjects: camera occlusion must not
    // remove a rear body from a dimension. The current reference set determines
    // which members exist; no FindOccurrence of every prototype body is allowed.
    public List<Tag> CollectVisibleBodyTags()
    {
        AssertContext();
        NXOpen.Layer.StateInfo[] layers;
        _assembly.Layers.GetVisibilitiesInView(View, out layers);
        var bodies = new HashSet<Tag>();
        var visited = new HashSet<Tag>();
        CollectMembers(_target, layers, bodies, visited, 0);
        AssertContext();
        if (bodies.Count == 0) throw new InvalidOperationException("当前显示范围没有可精确测量的实体。");
        RunLog.Write("[Measurement scope] DisplayedOccurrenceBodies=" + bodies.Count + " Target=" + _target.DisplayName);
        return new List<Tag>(bodies);
    }

    private bool Displayed(DisplayableObject obj, NXOpen.Layer.StateInfo[] layers)
    {
        if (obj.IsBlanked) return false;
        int layer = obj.Layer;
        if (layer < 1 || layer > layers.Length)
            throw new InvalidOperationException("无法确认对象的图层可见性: tag=" + obj.Tag);
        if (layers[layer - 1].State == NXOpen.Layer.State.Hidden) return false;
        for (Component c = obj as Component ?? obj.OwningComponent; c != null; c = c.Parent)
        {
            if (c.IsBlanked || c.IsSuppressed) return false;
            if (c.Layer < 1 || c.Layer > layers.Length)
                throw new InvalidOperationException("无法确认组件的图层可见性: tag=" + c.Tag);
            if (layers[c.Layer - 1].State == NXOpen.Layer.State.Hidden) return false;
        }
        return true;
    }

    private void CollectMembers(Component component, NXOpen.Layer.StateInfo[] layers,
        HashSet<Tag> bodies, HashSet<Tag> visited, int depth)
    {
        TraversalCancellation.Checkpoint();
        if (depth > 100) throw new InvalidOperationException("装配结构超过 100 层。");
        if (!Displayed(component, layers) || !visited.Add(component.Tag)) return;
        if (component.Tag == _root.Tag)
        {
            foreach (Body body in _assembly.Bodies) AddBody(body, layers, bodies);
            foreach (NXOpen.Facet.FacetedBody facet in _assembly.FacetedBodies) AddBody(facet, layers, bodies);
            foreach (Component child in component.GetChildren()) CollectMembers(child, layers, bodies, visited, depth + 1);
            return;
        }
        var members = new HashSet<Tag>();
        Tag member = Tag.Null;
        while (true)
        {
            UFSession.GetUFSession().Assem.CycleObjsInComp(component.Tag, ref member);
            if (member == Tag.Null) break;
            if (!members.Add(member)) throw new InvalidOperationException("组件成员枚举发生循环。");
            TraversalCancellation.Checkpoint();
            var obj = NXOpen.Utilities.NXObjectManager.Get(member) as DisplayableObject;
            if (obj == null) continue;
            var child = obj as Component;
            if (child != null) CollectMembers(child, layers, bodies, visited, depth + 1);
            else AddBody(WholeObject(obj), layers, bodies);
        }
        // Siemens uf_assem_eg3 cycles geometry and walks child components
        // separately; component members need not contain the child occurrences.
        foreach (Component child in component.GetChildren())
            CollectMembers(child, layers, bodies, visited, depth + 1);
    }

    private void AddBody(DisplayableObject obj, NXOpen.Layer.StateInfo[] layers, HashSet<Tag> bodies)
    {
        if (!IsModelGeometry(obj) || !InTarget(obj) || !Displayed(obj, layers)) return;
        var body = obj as Body;
        var facet = obj as NXOpen.Facet.FacetedBody;
        if (facet != null)
        {
            if (!facet.IsAssociatedBodyLoaded)
                throw new InvalidOperationException("显示对象只有轻量化/小平面数据，无法精确测量；未切换或完整加载原型: tag=" + facet.Tag);
            body = facet.AssociatedBody;
        }
        if (body == null || (body.OwningComponent == null && body.OwningPart != _assembly) || !InTarget(body))
            throw new InvalidOperationException("无法取得原装配中的实体实例，拒绝使用原型坐标代替: tag=" + obj.Tag);
        bodies.Add(body.Tag);
    }

    private void AssertContext()
    {
        View[] active = _assembly.Views.GetActiveViews();
        if (_session.Parts.Display != _assembly || _session.Parts.Work != _assembly ||
            _session.Parts.WorkComponent != _workComponent || _assembly.ModelingViews.WorkView.Tag != View.Tag ||
            active.Length != 1 || active[0].Tag != View.Tag)
            throw new InvalidOperationException("导出期间原装配/工作组件/视口意外变化。");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var errors = new List<Exception>();
        // Attempt every restoration category even when another category fails.
        // Re-hide the originally hidden path before showing other branches.
        foreach (bool blank in new[] { true, false })
        {
            try
            {
                var objects = new List<DisplayableObject>();
                foreach (Visibility entry in _changed.Values)
                    if (entry.Blanked == blank && entry.Object.IsBlanked != blank) objects.Add(entry.Object);
                ApplyVisibility(objects, blank, "Restore visibility " + blank);
            }
            catch (Exception ex) { errors.Add(ex); }
        }
        try { _state.Restore(); } catch (Exception ex) { errors.Add(ex); }
        try
        {
            _session.DisplayManager.MakeUpToDate();
            AssertContext();
            foreach (Visibility entry in _changed.Values)
                if (entry.Object.IsBlanked != entry.Blanked) throw new InvalidOperationException("显示状态未完整恢复。");
        }
        catch (Exception ex) { errors.Add(ex); }
        if (errors.Count > 0)
        {
            AssemblyTraverser.RequestStop();
            NxPartResources.KeepCurrentPartsOpen();
            foreach (Exception ex in errors) RunLog.Error("恢复装配内截图状态失败", ex);
            throw new InvalidOperationException("原装配截图状态未完整恢复，已停止并保留当前加载部件。", errors[0]);
        }
        RunLog.Write("[Export context restored] 原装配显示状态、相机、WCS/三重轴已恢复。");
    }
}
