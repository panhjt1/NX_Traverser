using System;
using System.Collections.Generic;
using NXOpen;
using NXOpen.Assemblies;

internal static class NxGeometry
{
    public static Part EnsurePrototype(Component component, ComponentAssembly assembly, bool fully)
    {
        Part part = component.Prototype as Part;
        if (part == null)
        {
            NxPartResources.BeforeLoad(null);
            ComponentAssembly.OpenComponentStatus[] openStatus = null;
            string stage = "OpenComponents " + component.DisplayName;
            RunLog.NxCall(stage, () =>
            {
                using (PartLoadStatus status = assembly.OpenComponents(ComponentAssembly.OpenOption.ComponentOnly,
                    new[] { component }, out openStatus))
                    NxPartContext.CheckLoadStatus(status, stage);
            });
            if (openStatus == null || openStatus.Length != 1 ||
                openStatus[0] != ComponentAssembly.OpenComponentStatus.SuccessfullyOpened)
                throw new InvalidOperationException("组件未成功打开或已在加载过程中被删除。");
            part = component.Prototype as Part;
        }
        if (part == null) throw new InvalidOperationException("无法读取组件原型: " + component.DisplayName);
        if (fully) LoadFully(part);
        else
        {
            NxPartResources.BeforeLoad(part);
            RunLog.NxCall("LoadThisPartPartially " + part.Name, () =>
            {
                using (PartLoadStatus status = part.LoadThisPartPartially())
                    NxPartContext.CheckLoadStatus(status, "LoadThisPartPartially " + part.Name);
                if (part.PartLoadState != PartLoadState.PartiallyLoaded && part.PartLoadState != PartLoadState.FullyLoaded)
                    throw new InvalidOperationException("部件显示数据未加载: " + part.Name + " state=" + part.PartLoadState);
            });
        }
        return part;
    }

    public static void LoadFully(Part part)
    {
        if (!part.IsFullyLoaded)
        {
            NxPartResources.BeforeLoad(part);
            bool modified = part.IsModified;
            RunLog.Write("[Load geometry] " + part.Name + " State=" + part.PartLoadState +
                " ReadOnly=" + part.IsReadOnly + " Modified=" + modified);
            RunLog.NxCall("LoadThisPartFully " + part.Name, () =>
            {
                using (PartLoadStatus status = part.LoadThisPartFully())
                    NxPartContext.CheckLoadStatus(status, "LoadThisPartFully " + part.Name);
                if (!part.IsFullyLoaded) throw new InvalidOperationException("几何未完整加载: " + part.Name);
            });
            RunLog.Write("[Load geometry done] " + part.Name + " State=" + part.PartLoadState +
                " Modified=" + part.IsModified);
            if (!modified && part.IsModified)
                RunLog.Write("[WARN] 加载几何后 NX 将部件标记为已修改: " + part.Name);
        }
        if (!part.IsFullyLoaded) throw new InvalidOperationException("几何未完整加载: " + part.Name);
    }

    // Screenshots need display data, not feature history or exact body queries.
    // Visit all unsuppressed descendants so an unloaded branch is not silently
    // omitted, but never force LoadThisPartFully just to count screenshot bodies.
    public static void PrepareDisplay(Part part)
    {
        Component root = part.ComponentAssembly.RootComponent;
        if (root == null) return;
        var ancestors = new HashSet<Tag> { part.Tag };
        foreach (Component child in root.GetChildren()) PrepareDisplay(child, part.ComponentAssembly, ancestors, 0);
    }

    private static void PrepareDisplay(Component component, ComponentAssembly assembly,
        HashSet<Tag> ancestors, int depth)
    {
        if (AssemblyTraverser.StopRequested) throw new OperationCanceledException("截图加载已停止。");
        if (component.IsSuppressed) return;
        if (depth > 100) throw new InvalidOperationException("装配结构超过 100 层。");
        Part part = EnsurePrototype(component, assembly, false);
        if (!ancestors.Add(part.Tag)) throw new InvalidOperationException("检测到循环装配引用。");
        try
        {
            foreach (Component child in component.GetChildren()) PrepareDisplay(child, assembly, ancestors, depth + 1);
        }
        finally { ancestors.Remove(part.Tag); }
    }

    // Whole selected prototype including unsuppressed descendant occurrences.
    // Never silently report an undersized box when a body cannot be represented.
    public static List<Tag> CollectBodyTags(Part part)
    {
        LoadFully(part);
        var tags = new List<Tag>();
        foreach (Body body in part.Bodies) if (body.Tag != Tag.Null) tags.Add(body.Tag);
        Component root = part.ComponentAssembly.RootComponent;
        if (root != null)
        {
            var ancestors = new HashSet<Tag> { part.Tag };
            foreach (Component child in root.GetChildren()) Collect(child, part.ComponentAssembly, tags, ancestors, 0);
        }
        return tags;
    }

    private static void Collect(Component component, ComponentAssembly assembly, List<Tag> tags,
        HashSet<Tag> ancestors, int depth)
    {
        if (AssemblyTraverser.StopRequested) throw new OperationCanceledException("遍历已停止。");
        if (component.IsSuppressed) return;
        if (depth > 100) throw new InvalidOperationException("装配结构超过 100 层。");
        Part part = EnsurePrototype(component, assembly, true);
        if (!ancestors.Add(part.Tag)) throw new InvalidOperationException("检测到循环装配引用。");
        try
        {
            foreach (Body body in part.Bodies)
            {
                NXObject occurrence = component.FindOccurrence(body);
                if (occurrence == null || occurrence.Tag == Tag.Null)
                    throw new InvalidOperationException("参考集未包含实体，不能输出完整装配结果: " + component.DisplayName);
                tags.Add(occurrence.Tag);
            }
            foreach (Component child in component.GetChildren()) Collect(child, assembly, tags, ancestors, depth + 1);
        }
        finally { ancestors.Remove(part.Tag); }
    }
}
