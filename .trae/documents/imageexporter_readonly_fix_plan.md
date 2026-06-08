# ImageExporter 只读对话框问题修复方案

## 问题分析

经过调试确认，只读对话框在 `ImageExporter.cs` 第65行 `theSession.Parts.SetDisplay()` 调用时弹出。虽然当前代码已有抑制只读警告的逻辑（`SuppressReadonlyWarning()`），但问题仍存在。

**根本原因**：
1. 当前的抑制逻辑仅使用 `UF_UI_SUPPRESS_READONLY_WARNING` 标志
2. 该标志可能无法完全抑制所有只读相关的对话框
3. 需要采用更全面的抑制策略

## 修复方案

### 修改文件
- `/workspace/ImageExporter.cs`

### 修改内容

1. **增强 SuppressReadonlyWarning 方法**：
   - 使用 `UF_UI_SUPPRESS_ALL_DIALOGS` 替代 `UF_UI_SUPPRESS_READONLY_WARNING`
   - 确保在方法开始时立即设置抑制，覆盖整个 Process 方法的执行期间

2. **优化抑制时机**：
   - 在 `Process` 方法入口处立即设置抑制
   - 在整个方法执行期间保持抑制状态
   - 仅在 finally 块中恢复原始状态

### 代码修改说明

```csharp
private int SuppressReadonlyWarning()
{
    int originalMode = 0;
    
    try
    {
        var ufSession = UFSession.GetUFSession();
        ufSession.UI.AskSuppressDialogs(out originalMode);
        
        // 使用更强大的 ALL_DIALOGS 抑制标志
        var flagField = typeof(UFConstants).GetField("UF_UI_SUPPRESS_ALL_DIALOGS");
        if (flagField != null)
        {
            int suppressFlag = (int)flagField.GetValue(null);
            ufSession.UI.SetSuppressDialogs(suppressFlag);
        }
    }
    catch { }
    
    return originalMode;
}
```

### 修改范围
- 仅修改 `ImageExporter.cs` 业务类内部
- 不修改主遍历程序 `AssemblyTraverser.cs`
- 不影响其他事务处理器

## 风险评估

| 风险点 | 风险等级 | 说明 |
|--------|----------|------|
| 过度抑制 | 中 | 使用 ALL_DIALOGS 可能抑制其他重要对话框 |
| 状态恢复失败 | 低 | finally 块确保状态恢复 |
| UF API 调用失败 | 低 | 已有异常处理机制 |

## 验证方法

1. 在只读权限的装配环境中运行截图功能
2. 确认不再弹出"该部件是只读的"对话框
3. 确认截图功能正常工作
4. 确认其他对话框（如错误提示）仍能正常显示