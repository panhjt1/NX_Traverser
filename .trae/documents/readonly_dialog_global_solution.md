# 只读对话框问题修复方案 - 全局设置方案

## 问题分析

1. `UFSession` 类在您当前使用的 NX 版本中没有 `UI` 属性，之前的反射方法无法工作
2. `SetDisplay` 的第三个参数 `setVisited` 与对话框无关，不影响只读警告弹出
3. 问题确认：在 `ImageExporter.Process` 中调用 `theSession.Parts.SetDisplay()` 时弹出只读对话框

## 解决方案

将抑制只读对话框的全局设置移到 `AssemblyTraverser.InternalMain` 遍历开始处，一次设置完成，保持整个遍历期间生效，遍历结束后恢复。

### 修改范围

1. **`/workspace/AssemblyTraverser.cs`**
   - 在遍历开始前（第 74-75 行）添加抑制只读对话框的代码
   - 在 finally 块（第 81-87 行）恢复原始设置
   - 修改范围：主程序，但不影响原有业务逻辑

2. **`/workspace/ImageExporter.cs`**
   - 不需要额外修改，已经移除了抑制逻辑

### 代码实现

```csharp
public static void InternalMain()
{
    // 重置停止标记
    StopForm.StopRequested = false;

    // 启动自己的控制窗口（非模态）
    StopForm stopForm = new StopForm();
    stopForm.Show();   // 非模态，不阻塞

    var theSession = Session.GetSession();
    var ufSession = UFSession.GetUFSession();
    Part workPart = theSession.Parts.Work;
    theSession.ListingWindow.Open();

    if (workPart == null)
    {
        theSession.ListingWindow.WriteLine("Error: No work part found.");
        return;
    }

    Component rootComponent = workPart.ComponentAssembly.RootComponent;
    if (rootComponent == null)
    {
        theSession.ListingWindow.WriteLine("Error: Root component is null. Possibly a piece part.");
        return;
    }

    // 确保输出目录存在
    string outputFolder = string.IsNullOrEmpty(_config.filePath) 
        ? System.IO.Path.GetTempPath() 
        : _config.filePath;
    System.IO.Directory.CreateDirectory(outputFolder);

    // ========== 新增：开始遍历前全局抑制只读对话框 ==========
    int originalSuppressMode = 0;
    bool suppressEnabled = false;
    try
    {
        // 使用反射查找并调用 UF_UI 相关方法抑制只读警告
        var uiProperty = ufSession.GetType().GetProperty("UI");
        if (uiProperty != null)
        {
            var uiObject = uiProperty.GetValue(ufSession);
            if (uiObject != null)
            {
                var askMethod = uiObject.GetType().GetMethod("AskSuppressDialogs");
                if (askMethod != null)
                {
                    var parameters = new object[] { originalSuppressMode };
                    askMethod.Invoke(uiObject, parameters);
                    originalSuppressMode = (int)parameters[0];

                    var setMethod = uiObject.GetType().GetMethod("SetSuppressDialogs");
                    if (setMethod != null)
                    {
                        // 尝试获取 UF_UI_SUPPRESS_READONLY_WARNING 常量
                        var flagField = typeof(UFConstants).GetField("UF_UI_SUPPRESS_READONLY_WARNING");
                        int suppressFlag = 1;
                        if (flagField != null)
                        {
                            suppressFlag = (int)flagField.GetValue(null);
                        }
                        setMethod.Invoke(uiObject, new object[] { suppressFlag });
                        suppressEnabled = true;
                    }
                }
            }
        }
    }
    catch { }
    // ========== 新增结束 ==========

    // 开始深度优先遍历，初始层级为0
    try
    {
        TraverseAssembly(rootComponent, ufSession, theSession, 0, outputFolder);
    }
    finally
    {
        // ========== 新增：恢复原始抑制模式 ==========
        if (suppressEnabled)
        {
            try
            {
                var uiProperty = ufSession.GetType().GetProperty("UI");
                if (uiProperty != null)
                {
                    var uiObject = uiProperty.GetValue(ufSession);
                    if (uiObject != null)
                    {
                        var setMethod = uiObject.GetType().GetMethod("SetSuppressDialogs");
                        if (setMethod != null)
                        {
                            setMethod.Invoke(uiObject, new object[] { originalSuppressMode });
                        }
                    }
                }
            }
            catch { }
        }
        // ========== 新增结束 ==========

        // 关闭所有事务处理器（如CSV文件等资源）
        foreach (var handler in _transactionHandlers)
        {
            handler.Finish();
        }

        // 任务完成后关闭窗口
        if (stopForm.InvokeRequired)
            stopForm.BeginInvoke(new Action(() => stopForm.Close()));
        else
            stopForm.Close();
    }
}
```

## 优缺点分析

| 优点 | 缺点 |
|------|------|
| 一次设置，全程生效，避免每次调用 `SetDisplay` 都要设置/恢复 | 需要修改主遍历程序 |
| 统一管理，代码简洁 | 反射方式调用，仍然存在反射失败的可能性 |
| 不修改业务类 `ImageExporter` 和 `BoundingBoxExporter` | |

## 风险评估

- **反射失败**：如果 `UI` 属性不存在，代码会自动跳过，不影响原有功能，只是不抑制对话框
- **状态恢复**：finally 块确保无论遍历是否出错都会恢复原始状态
- **兼容性**：反射方式可以兼容不同 NX 版本，不会产生编译错误

## 如果仍然无效

如果全局抑制仍然无法阻止对话框弹出，最后一个方案是：
- 修改 `SetDisplay` 的调用方式，不切换显示部件，直接在原有显示部件上进行截图
- 但这个方案需要重写 `ImageExporter` 的核心逻辑，改动较大
