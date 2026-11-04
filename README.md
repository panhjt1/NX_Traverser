# NX_Traverser

在 Siemens NX / Teamcenter 集成环境遍历装配，导出多视角 PNG 和 ACS/WCS 包围盒 CSV。此分支面向 **NX 2512，C# .NET Framework（默认 4.8，可显式试编译 4.7.2），Windows x64**。

当前是供本地编译和验收的源码版本：DLX/XML、核心数学与文本回归已检查，NX SDK 编译和 Teamcenter 实机测试尚待完成。

## 本地编译与部署

云端更新推送到 `fix/nx2512-teamcenter-readonly` 分支并维护提示词文档；用户自行在 GitHub 页面下载 ZIP。本地 agent 无需 Git 联网。每次解压到新目录并保留旧目录的本地修改，记录页面上的提交号；在含 `NX_Traverser.csproj` 的目录中构建。

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
- 八个 PNG 视角：Trimetric、Isometric、Top、Front、Right、Back、Bottom、Left。按普通图形区域大小输出透明 PNG、边缘增强、自动适配；截图隐藏 WCS 和三重轴，成功/失败/取消后恢复各自原有可见性及视图。
- 截图在原装配现有视口中临时只显示目标实例，不激活原型、不创建隔离窗口；之后恢复原显示状态再卸载临时部件。按目标自身 ACS 补偿实例放置旋转，保持零件/子装配的标准视角，不依赖当前 WCS。
- 图片采用首次匹配实例当前参考集和隐藏状态。要求原装配同时为 work/display、普通单建模视口、非爆炸视图；镜像、剪切、非均匀缩放拒绝。实际方向、内容和显示恢复仍需本地验证。
- 包围盒按**目标原型的 ACS/WCS**测量。选中子装配时包含未抑制后代实体在该子装配中的放置；不是顶层装配的每实例坐标报表。参考集缺体、未完整加载或任一实体计算失败时，记录失败而不写不完整尺寸。
- 每次新建 `NX_Traverser_日期_时间_随机后缀` 子目录，含 PNG、`BoundingBoxResult.csv`（有成功测量时）及 `Traversal.log`，保留以前结果。
- CSV 首五列为 `零件名,ID,x,y,z`，后接 `单位,坐标系,范围,实体数`；使用 UTF-8 BOM、固定小数点及标准引号转义。

## Teamcenter 只读处理

仅截图已移除此前触发提示的 `SetActiveDisplay` 路径：临时隐藏其他分支和上级自带几何，部分加载目标实例，调整原装配相机后截图。显示/隐藏和加载仍可能触发 NX 或站点回调，**尚未完成无弹窗实机验收**。精确测量仍需要原型激活，组合运行的测量阶段仍可能提示。`641058` 记录为警告并检查实际加载状态；其他加载错误仍失败。不保存、不签出、不修改权限、不清除修改标记。

每个遍历节点在恢复上下文后释放临时加载部件，失败/取消路径也清理。原先干净的部分/最小加载部件被本次使用或提升加载后也允许卸载；原有完整加载、已修改、显示/工作部件及正在遍历的祖先会保留。逐个关闭并丢弃本次临时内存变化，不递归关闭整树；关闭失败则停止继续加载。日志记录关闭前后和进程私有内存。单个匹配大装配仍需容纳该装配的数据，NX 缓存也可能使进程内存不立即下降。

关闭后重新枚举会话，不访问被父部件关闭连带失效的旧对象；只有确认目标已离开会话才容忍关闭时的 3615013。截图恢复 blank 状态、相机和坐标轴开关并核验原 work/display/视口后才卸载；恢复失败则停止并保留当前加载集合。Restore visibility 可能隐式加载显示数据，需结合日志检查峰值，不能只看最后已加载数。没有验证到保证所有 NX/Teamcenter 回调无弹窗的公开开关。日志保留成对 begin/end；显示切换可能重置 undo 标记，不删除用户撤销记录。具体 API 依据见 [NX2512_API_REVIEW.md](docs/NX2512_API_REVIEW.md)。

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

旧配置属性 `useIsolatedImageView` 默认 false，仅保留调用兼容。指定 true 会在运行前明确拒绝；请删除旧调用中的 true 设置。合并 ZIP 时删除旧 `NxImageContext.cs`，保留 `NxViewState.cs` 和新增 `NxAssemblyImageContext.cs`，避免通配编译带入废弃代码。

## 独立检查

```text
python tests/validate_dlx.py
dotnet run --project tests/CoreTests.csproj -c Release
```

第二项只需 .NET 8 SDK、不依赖 NX 或 NuGet 包，测试旋转坐标系、多实体合并、异常数据、CSV/文件名和部件释放所有权规则。它不替代 NX 2512 编译或 Teamcenter 验收。
