# 修复只读部件对话框问题计划

## 问题描述
使用 UI Styler 运行时，每个只读部件都会弹出 "该部件是只读的" 提示窗口，打断自动化执行。

## 问题根源
在 **ImageExporter.cs** 中，修改只读部件的 `WCS.Visibility` 属性会触发只读警告对话框。

## 用户要求
1. 保留 `WCS.Visibility` 修改
2. 不修改主遍历程序 AssemblyTraverser.cs
3. 只在业务类（ImageExporter.cs）内部解决

## 解决方案

### 方案：在 ImageExporter 中设置会话级别的只读抑制选项

在 ImageExporter 构造函数或 Process 方法开头，通过 NX 会话选项设置只读抑制。

### 具体修改

#### ImageExporter.cs 修改

1. **在构造函数中添加会话设置**：
   - 在类级别添加静态标志位，确保只设置一次
   - 使用 `Session.SetUserFunction()` 或 `UF` API 设置只读抑制选项
   - 或者使用 `Part.IsModifiable` 检查后再修改

2. **可能的 NX API 调用**：
   - 检查 `Part.IsModifiable` 属性
   - 使用 UF（User Function）级别的 API 可能避免对话框
   - 设置会话的 "Suppress Part Readonly Warning" 选项

### 待确认
需要用户提供 NX 版本号，以便查找正确的 API 调用方式。
