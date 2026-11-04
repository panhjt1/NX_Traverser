using System;
using System.Collections.Generic;
using NXOpen;
using NXOpen.Assemblies;
using NXOpen.UF;

internal static class NxGeometry
{
    public static Part EnsurePrototype(Component component, ComponentAssembly assembly)
    {
        TraversalCancellation.Checkpoint();
        Part part = component.Prototype as Part;
        if (part == null)
        {
            NxPartResources.BeforeLoad(null);
            ComponentAssembly.OpenComponentStatus[] openStatus = null;
            string stage = "OpenComponents " + component.DisplayName;
            TraversalCancellation.NxCall(stage, () =>
            {
                using (PartLoadStatus status = assembly.OpenComponents(ComponentAssembly.OpenOption.ComponentOnly,
                    new[] { component }, out openStatus))
                    CheckLoadStatus(status, stage);
            });
            if (openStatus == null || openStatus.Length != 1 ||
                openStatus[0] != ComponentAssembly.OpenComponentStatus.SuccessfullyOpened)
                throw new InvalidOperationException("组件未成功打开或已在加载过程中被删除。");
            part = component.Prototype as Part;
        }
        if (part == null) throw new InvalidOperationException("无法读取组件原型: " + component.DisplayName);
        // Register use even when no load API is needed, so ownership/unload
        // rules for initially clean partial parts are unchanged.
        NxPartResources.BeforeLoad(part);
        if (part.PartLoadState == PartLoadState.PartiallyLoaded || part.PartLoadState == PartLoadState.FullyLoaded)
            return part;
        TraversalCancellation.NxCall("LoadThisPartPartially " + part.Name, () =>
        {
            using (PartLoadStatus status = part.LoadThisPartPartially())
                CheckLoadStatus(status, "LoadThisPartPartially " + part.Name);
            if (part.PartLoadState != PartLoadState.PartiallyLoaded && part.PartLoadState != PartLoadState.FullyLoaded)
                throw new InvalidOperationException("部件显示数据未加载: " + part.Name + " state=" + part.PartLoadState);
        });
        return part;
    }

    // Both exporters share display preparation without forcing feature history.
    // Visit visible, unsuppressed descendants without forcing full geometry.
    // Hidden branches keep their original display and loading state.
    public static void PrepareDisplay(Component target, ComponentAssembly assembly)
    {
        // Walk the selected occurrence, preserving its reference set and hidden
        // descendants. Never load siblings just to manipulate their visibility.
        PrepareDisplay(target, assembly, new HashSet<Tag>(), 0);
    }

    private static void PrepareDisplay(Component component, ComponentAssembly assembly,
        HashSet<Tag> ancestors, int depth)
    {
        TraversalCancellation.Checkpoint();
        if (component.IsSuppressed || component.IsBlanked) return;
        if (depth > 100) throw new InvalidOperationException("装配结构超过 100 层。");
        Part part = EnsurePrototype(component, assembly);
        if (!ancestors.Add(part.Tag)) throw new InvalidOperationException("检测到循环装配引用。");
        try
        {
            foreach (Component child in component.GetChildren()) PrepareDisplay(child, assembly, ancestors, depth + 1);
        }
        finally { ancestors.Remove(part.Tag); }
    }

    public static void CheckLoadStatus(PartLoadStatus status, string stage)
    {
        if (status == null || status.NumberUnloadedParts == 0) return;
        bool failed = false;
        for (int i = 0; i < status.NumberUnloadedParts; ++i)
        {
            int code = status.GetStatus(i);
            // UF_PART_err_read_only_modified: retrieval changed a read-only part.
            // Exact geometry queries are checked separately; never hide other errors.
            bool readOnlyWarning = code == 641058;
            RunLog.Write((readOnlyWarning ? "[Load warning] " : "[Load error] ") + stage + ": " +
                status.GetPartName(i) + " code=" + code + " " + status.GetStatusDescription(i));
            if (!readOnlyWarning) failed = true;
        }
        if (failed) throw new InvalidOperationException(stage + " 加载失败，已跳过，详情见日志。");
    }

    internal static DisplayableObject WholeObject(DisplayableObject obj)
    {
        var face = obj as Face;
        if (face != null) return face.GetBody();
        var edge = obj as Edge;
        return edge != null ? edge.GetBody() : obj;
    }

    internal static bool IsModelGeometry(DisplayableObject obj)
    {
        return obj is Body || obj is NXOpen.Facet.FacetedBody;
    }

    private sealed class VisibleBodyCollector
    {
        private readonly NxAssemblyExportContext _context;
        public VisibleBodyCollector(NxAssemblyExportContext context) { _context = context; }
        // Use occurrence members, not AskVisibleObjects: camera occlusion must not
        // remove a rear body from a dimension. The current reference set determines
        // which members exist; no FindOccurrence of every prototype body is allowed.
        public List<Tag> Collect()
        {
            _context.AssertContext();
            NXOpen.Layer.StateInfo[] layers;
            _context.AssemblyPart.Layers.GetVisibilitiesInView(_context.View, out layers);
            var bodies = new HashSet<Tag>();
            var visited = new HashSet<Tag>();
            CollectMembers(_context.Target, layers, bodies, visited, 0);
            _context.AssertContext();
            if (bodies.Count == 0) throw new InvalidOperationException("当前显示范围没有可精确测量的实体。");
            RunLog.Write("[Measurement scope] DisplayedOccurrenceBodies=" + bodies.Count + " Target=" + _context.Target.DisplayName);
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
            if (component.Tag == _context.AssemblyPart.ComponentAssembly.RootComponent.Tag)
            {
                foreach (Body body in _context.AssemblyPart.Bodies) AddBody(body, layers, bodies);
                foreach (NXOpen.Facet.FacetedBody facet in _context.AssemblyPart.FacetedBodies) AddBody(facet, layers, bodies);
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
            if (!IsModelGeometry(obj) || !_context.InTarget(obj) || !Displayed(obj, layers)) return;
            var body = obj as Body;
            var facet = obj as NXOpen.Facet.FacetedBody;
            if (facet != null)
            {
                if (!facet.IsAssociatedBodyLoaded)
                    throw new InvalidOperationException("显示对象只有轻量化/小平面数据，无法精确测量；未切换或完整加载原型: tag=" + facet.Tag);
                body = facet.AssociatedBody;
            }
            if (body == null || (body.OwningComponent == null && body.OwningPart != _context.AssemblyPart) || !_context.InTarget(body))
                throw new InvalidOperationException("无法取得原装配中的实体实例，拒绝使用原型坐标代替: tag=" + obj.Tag);
            bodies.Add(body.Tag);
        }

    }
    internal static BoundingBoxResult Measure(NxAssemblyExportContext context, string mode)
    {
        TraversalCancellation.Checkpoint();
        Part part = context.TargetPart;
        if (part == null) throw new InvalidOperationException("无法取得测量目标。");
        List<Tag> bodies = new VisibleBodyCollector(context).Collect();
        UFSession uf = UFSession.GetUFSession();
        Tag temporaryCsys = Tag.Null, temporaryMatrix = Tag.Null;
        var localOrigin = new double[3];
        var localAxes = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        if (mode == "WCS")
        {
            // Read the target's WCS without activating it or changing its WCS.
            Tag matrix;
            uf.Csys.AskCsysInfo(part.WCS.CoordinateSystem.Tag, out matrix, localOrigin);
            uf.Csys.AskMatrixValues(matrix, localAxes);
        }
        double unitScale = ExportUtilities.UnitScale(context.AssemblyPart.PartUnits.ToString(), part.PartUnits.ToString());
        // UF placement translation is in assembly units, WCS origin in target units.
        for (int i = 0; i < localOrigin.Length; ++i) localOrigin[i] /= unitScale;
        double[] origin, axes;
        ExportUtilities.FrameInAssembly(localOrigin, localAxes, context.Transform, out origin, out axes);
        var box = new BoundingBoxMath();
        try
        {
            // Occurrence geometry and query CSYS must both be in current work
            // assembly space. NULL_TAG would mean its WCS, not the target ACS.
            uf.Csys.CreateMatrix(axes, out temporaryMatrix);
            uf.Csys.CreateTempCsys(origin, temporaryMatrix, out temporaryCsys);
            foreach (Tag body in bodies)
            {
                TraversalCancellation.Checkpoint();
                var corner = new double[3];
                var directions = new double[3, 3];
                var distances = new double[3];
                RunLog.Write("[BoundingBox " + mode + "] body=" + body);
                NxPartResources.BeforeLoad(null); // Exact query may retrieve geometry internally.
                TraversalCancellation.NxCall("AskBoundingBoxExact occurrence=" + body,
                    () => uf.Modl.AskBoundingBoxExact(body, temporaryCsys, corner, directions, distances));
                // Query aligned geometry directly; rotating an ACS AABB is not exact.
                box.Add(corner, directions, distances, origin, axes);
            }
        }
        finally
        {
            // Only delete temporary objects created here; never delete the actual WCS.
            var cleanupErrors = new List<Exception>();
            foreach (Tag tag in new[] { temporaryCsys, temporaryMatrix })
            {
                try { if (tag != Tag.Null) uf.Obj.DeleteObject(tag); }
                catch (Exception ex) { cleanupErrors.Add(ex); }
            }
            if (cleanupErrors.Count > 0)
            {
                AssemblyTraverser.RequestStop();
                NxPartResources.KeepCurrentPartsOpen();
                foreach (Exception ex in cleanupErrors) RunLog.Error("删除临时测量坐标系/矩阵失败", ex);
                throw new InvalidOperationException("临时测量对象未完整清理，已停止。", cleanupErrors[0]);
            }
        }
        TraversalCancellation.Checkpoint();
        double[] dimensions = box.Dimensions();
        for (int i = 0; i < dimensions.Length; ++i) dimensions[i] *= unitScale;
        return new BoundingBoxResult(dimensions, part.PartUnits.ToString(), mode, bodies.Count);
    }
}
