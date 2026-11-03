# NX_Traverser

在 Siemens NX / Teamcenter 集成环境遍历装配，导出多视角 PNG 和 ACS/WCS 包围盒 CSV。此分支面向 **NX 2512，C# .NET Framework（默认 4.8，可显式试编译 4.7.2），Windows x64**。

当前是供本地编译和验收的源码版本：DLX/XML、核心数学与文本回归已检查，NX SDK 编译和 Teamcenter 实机测试尚待完成。

## 本地编译与部署

将 [LOCAL_AGENT_PROMPT.md](LOCAL_AGENT_PROMPT.md) 交给本地 agent；其中包含取代码、构建、签名、部署及只读环境验收步骤，并在每次修改后维护。

```powershell
.\scripts\build.ps1 -NXInstallDir 'C:\Program Files\Siemens\NX2512' -Configuration Release
```

默认要求 VS 2022 / Build Tools 与 .NET Framework 4.8 Developer/Targeting Pack。复用原工程 4.7.2 目标时，可加 `-TargetFrameworkVersion v4.7.2`；需要对应开发包及支持 C# 7.3 的编译器。这个选项不改变 NX 2512 的运行环境要求；若 NX 引用要求更高框架，构建必须先解决该错误。项目引用本机 `NXBIN/managed` 中的 NXOpen、NXOpenUI 和 NXOpen.UF，并嵌入本机签名资源。不要混用 1946 或 `managed_core` DLL，不要随插件部署 Siemens DLL。

默认产物位于 `bin/Release/v4.8/`；显式选择 4.7.2 时位于 `bin/Release/v4.7.2/`。

将编译并按站点要求签名的 `NX_Traverser.dll` 与 `ClassLibrary1.dlx` 放在一起，从 NX 的“文件 → 执行 → NX Open/Designcenter Open”加载 DLL。入口为 `ClassLibrary1.Main`。本项目是多文件类库，不应把 `UIscripts.cs` 单独作为 Journal 播放。

DLX 已修复 XML 声明位置并保留原始控件 schema。其 `NX="1946"` 标记记录生成来源，不是运行时版本开关；请用 NX 2512 Block UI Styler 验证并按需另存。

## 功能与结果语义

- 名称读取 `DB_PART_NAME`，缺失时使用组件显示名；名称/ID 通配符列表按 OR 匹配，支持 `*`、`?`。空白行忽略；无过滤时处理叶子。
- 相同原型只导出一次；匹配装配后不会再单独导出后代。根节点深度为 0，最大深度默认 5，可通过 `TraversalConfig.maxDepth` 调整。
- 八个 PNG 视角：Trimetric、Isometric、Top、Front、Right、Back、Bottom、Left。透明背景；保留当前 WCS/三轴显示状态，结束恢复原视图。
- 包围盒按**目标原型的 ACS/WCS**测量。选中子装配时包含未抑制后代实体在该子装配中的放置；不是顶层装配的每实例坐标报表。参考集缺体、未完整加载或任一实体计算失败时，记录失败而不写不完整尺寸。
- 每次新建 `NX_Traverser_日期_时间_随机后缀` 子目录，含 PNG、`BoundingBoxResult.csv`（有成功测量时）及 `Traversal.log`，保留以前结果。
- CSV 首五列为 `零件名,ID,x,y,z`，后接 `单位,坐标系,范围,实体数`；使用 UTF-8 BOM、固定小数点及标准引号转义。

## Teamcenter 只读处理

已移除截图时对源 WCS 可见性的写入、源几何清理、遍历后主动关闭组件，以及无效的警告抑制代码。只有 OK 成功提交且 Block Styler 对话框释放后才执行任务，取消不会执行旧配置。不保存、不签出、不修改权限、不清除模型修改标记。

必要的加载、显示切换和视图旋转仍需站点实测。官方指出显示切换可能重置 undo 标记，恢复工作/显示上下文不等于恢复撤销历史。不自动关闭加载部件会增加会话内存占用。具体原因分析和 API 链接见 [NX2512_API_REVIEW.md](docs/NX2512_API_REVIEW.md)。

## 直接调用

从已加载 NX 的程序中调用（所有 NX API 留在 NX 主线程）：

```csharp
AssemblyTraverser.Run(new TraversalConfig {
    filePath = @"D:\output",
    namePatterns = new[] { "*总成*" },
    idPatterns = new string[0],
    flagImageOn = true,
    viewList = new[] { ImageExporter.SnapViewType.Isometric, ImageExporter.SnapViewType.Top },
    flagXYZOn = true,
    coordinateSelection = "WCS",
    maxDepth = 5
});
```

`AssemblyTraverser.RequestStop()` 仅为外部协作式取消接口，不能中断正在阻塞的 NX API；本仓库不包含停止窗口。

## 独立检查

```text
python tests/validate_dlx.py
dotnet run --project tests/CoreTests.csproj -c Release
```

第二项只需 .NET 8 SDK、不依赖 NX 或 NuGet 包，测试旋转坐标系、多实体合并、异常数据、CSV 与文件名处理。它不替代 NX 2512 编译或 Teamcenter 验收。
