# 修复只读部件对话框问题计划

## 问题描述
使用 UI Styler 运行时，每个只读部件都会弹出 "该部件是只读的" 提示窗口，打断自动化执行。

## 问题根源（已确认！）
在 **ImageExporter.cs** 的 `Process` 方法中，当将只读部件设置为显示部件时，NX 会弹出只读警告对话框。具体位置是调用 `theSession.Parts.SetDisplay(part, false, false, out loadStatus);` 时触发。

## 用户要求
1. 保留原有功能逻辑（包括 `WCS.Visibility` 修改）
2. 不修改主遍历程序 AssemblyTraverser.cs
3. 只在业务类（ImageExporter.cs）内部解决

## 解决方案

### 方案：在 SetDisplay 调用前设置只读抑制

使用 NX 的 UF API 在调用 SetDisplay 之前临时设置只读警告抑制。

### 具体修改

#### ImageExporter.cs 修改

1. **在 Process 方法中修改**：
   - 在调用 `SetDisplay` 之前使用 UF API 设置只读抑制
   - 使用反射方式调用，确保兼容性
   - 在 finally 块中恢复状态

2. **可能的 NX API 调用**：
   - `ufSession.UI.SetSuppressDialogs()` 设置对话框抑制
   - 使用 `UFConstants.UF_UI_SUPPRESS_READONLY_WARNING` 标志

## 风险分析
| 风险 | 缓解措施 |
|------|----------|
| 抑制的警告可能掩盖真实问题 | 只在必要时临时抑制，操作完成后恢复 |
| API 调用失败 | 使用 try-catch 确保主流程不受影响 |

## 验证方法
在包含只读部件的装配中运行程序，验证无弹窗打断。
