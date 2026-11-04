using System;
using System.Collections.Generic;
using NXOpen;
using NXOpen.Assemblies;

internal static class NxGeometry
{
    public static Part EnsurePrototype(Component component, ComponentAssembly assembly)
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
                    CheckLoadStatus(status, stage);
            });
            if (openStatus == null || openStatus.Length != 1 ||
                openStatus[0] != ComponentAssembly.OpenComponentStatus.SuccessfullyOpened)
                throw new InvalidOperationException("组件未成功打开或已在加载过程中被删除。");
            part = component.Prototype as Part;
        }
        if (part == null) throw new InvalidOperationException("无法读取组件原型: " + component.DisplayName);
        NxPartResources.BeforeLoad(part);
        RunLog.NxCall("LoadThisPartPartially " + part.Name, () =>
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
        if (AssemblyTraverser.StopRequested) throw new OperationCanceledException("导出加载已停止。");
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

}
