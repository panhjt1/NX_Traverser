# 只读对话框问题最终方案

## 根本原因分析

问题关键：
1. **没有 UIstyler 时**：`SetDisplay` 调用不触发只读警告
2. **使用 UIstyler 时**：同样的 `SetDisplay` 调用触发只读警告

这说明问题不在 `SetDisplay` 本身，而在于**调用时的 NX UI 状态不同**！

## 最终解决方案

**唯一彻底方案**：**完全不切换显示部件！**

当前 `ImageExporter` 的逻辑：
```
1. 保存当前显示部件
2. SetDisplay(part) → 触发警告！
3. 截图...
4. SetDisplay(originalDisplayPart)
```

更好的做法是直接操作部件而不切换显示状态！
