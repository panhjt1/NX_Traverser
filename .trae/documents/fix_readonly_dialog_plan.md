# 修复只读部件对话框问题计划

## 问题描述
使用 UI Styler 运行时，每个只读部件都会弹出 "该部件是只读的" 提示窗口，打断自动化执行。

## 问题根源（已找到！）
在 **ImageExporter.cs** 中，这两行代码对只读部件有问题：
```csharp
part.WCS.Visibility = false;
part.WCS.Visibility = true;
```
对于只读部件，修改 `WCS.Visibility` 属性会触发只读警告对话框！

## 分析：为什么无 UI 时没问题？
原始代码同样有这个问题，但可能是 UI 运行方式让 NX 的警告对话框显示行为不同。

## 解决方案

### 核心思路
避免在只读部件上直接修改 `WCS.Visibility`，改用其他方式或临时抑制警告。

### 修改文件
1. **AssemblyTraverser.cs** - 在程序开始和结束处添加会话设置
2. **ImageExporter.cs** - 避免修改只读部件属性
3. **BoundingBoxExporter.cs** - 优化部件加载和关闭逻辑

### 具体修改

#### 1. AssemblyTraverser.cs 修改
- 在 `InternalMain()` 开头：
  - 保存当前会话设置
  - 抑制只读警告对话框
  - 禁用自动保存提示
- 在 `finally` 块中恢复所有设置

#### 2. ImageExporter.cs 修改
- 移除或替换 `WCS.Visibility` 的修改
- 使用更安全的方式处理视图设置

#### 3. BoundingBoxExporter.cs 修改
- 优化部件关闭参数
- 确保不触发保存对话框

## 风险分析
| 风险 | 缓解措施 |
|------|----------|
| 抑制的警告可能掩盖真实问题 | 只在运行期间临时抑制，结束后恢复 |
| 会话状态改变 | 在 finally 块中恢复所有设置 |

## 验证方法
在包含只读部件的装配中运行程序，验证无弹窗打断。
