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
- 八个 PNG 视角：Trimetric、Isometric、Top、Front、Right、Back、Bottom、Left。按普通图形区域大小输出透明 PNG、边缘增强、自动适配；截图隐藏 WCS、三重轴及参考坐标系/点/草图/基准等辅助对象，成功/失败/取消后恢复各自原有可见性及视图。
- 截图在原装配现有视口中临时只显示目标实例，不激活原型、不创建隔离窗口；之后恢复原显示状态再卸载临时部件。按目标自身 ACS 补偿实例放置旋转，保持零件/子装配的标准视角，不依赖当前 WCS。
- 图片采用首次匹配实例当前参考集和隐藏状态。要求原装配同时为 work/display、普通单建模视口、非爆炸视图；镜像、剪切、非均匀缩放拒绝。实际方向、内容和显示恢复仍需本地验证。
- 包围盒在原装配中查询实体实例，方向按**目标自身 ACS/WCS**。仅计入当前显示范围的模型实体，排除隐藏/抑制、参考集外和隐藏图层对象；未隐藏但被其他实体遮挡的对象仍计入。截图与 XYZ 共用一次显示范围准备和恢复。没有精确实体或任一查询失败时，测量报错且不写不完整尺寸，不激活原型回退。结果单位为目标部件单位，CSV 范围为 `DisplayedOccurrenceBodies`。
- 每次新建 `NX_Traverser_日期_时间_随机后缀` 子目录，含 PNG、`BoundingBoxResult.csv`（有成功测量时）及 `Traversal.log`，保留以前结果。
- 排障优先查看输出目录里的 `Traversal.log` 尾部；它逐行落盘并保留完整异常链。开头的 Build/MVID 和 Assembly 路径可确认实际加载的 DLL；实体序号、所属组件和临时对象清理状态用于定位阶段，信息窗口复制的开头可能不包含真正报错。
- CSV 首五列为 `零件名,ID,x,y,z`，后接 `单位,坐标系,范围,实体数`；使用 UTF-8 BOM、固定小数点及标准引号转义。

## Teamcenter 只读处理

截图和 XYZ 均已移除此前触发提示的 `SetActiveDisplay` 路径：临时隐藏其他分支和上级自带几何，部分加载目标实例，调整原装配相机后截图。显示/隐藏和加载仍可能触发 NX 或站点回调，**尚未完成无弹窗实机验收**。精确测量使用原装配中的临时坐标系；原生查询可能内部加载数据，仍需实机验证。`641058` 记录为警告并检查实际加载状态；其他加载错误仍失败。不保存、不签出、不修改权限、不清除修改标记。

每个遍历节点在恢复上下文后释放临时加载部件，失败/取消路径也清理。原先干净的部分/最小加载部件被本次使用或提升加载后也允许卸载；原有完整加载、已修改、显示/工作部件及正在遍历的祖先会保留。逐个关闭并丢弃本次临时内存变化，不递归关闭整树；关闭失败则停止继续加载。日志记录关闭前后和进程私有内存。单个匹配大装配仍需容纳该装配的数据，NX 缓存也可能使进程内存不立即下降。

关闭后重新枚举会话，不访问被父部件关闭连带失效的旧对象；只有确认目标已离开会话才容忍关闭时的 3615013。截图恢复 blank 状态、相机和坐标轴开关并核验原 work/display/视口后才卸载；恢复失败则停止并保留当前加载集合。Restore visibility 可能隐式加载显示数据，需结合日志检查峰值，不能只看最后已加载数。没有验证到保证所有 NX/Teamcenter 回调无弹窗的公开开关。日志保留成对 begin/end；不删除用户撤销记录。具体 API 依据见 [NX2512_API_REVIEW.md](docs/NX2512_API_REVIEW.md)。

## 主循环与事务规范

保留“主循环 + `ITransactionHandler` 事务列表”的组织方式。事务统一实现 `Process(TransactionContext context)` 和 `Finish()`，主循环按登记顺序执行。

| 位置 | 负责什么 |
|---|---|
| `AssemblyTraverser` | 注册、筛选、递归、原型去重、逐事务异常/取消处理、成功计数、上下文生命周期、逐节点卸载、整轮 Finish |
| `TransactionContext` | 借给事务的目标信息、输出目录/统一文件名、日志/取消入口和公共服务；同一目标共用一个实例 |
| `NxAssemblyExportContext` | 按需准备原装配显示范围；视角、辅助对象及坐标轴处理；每个事务恢复画面，目标结束恢复分支显示 |
| `NxGeometry` | 按需部分加载、显示实体枚举、ACS/WCS 变换、精确查询和临时测量对象清理 |
| `ImageExporter` / `BoundingBoxExporter` | 分别组织视角并写 PNG、将测量结果写 CSV；只释放自己创建的 Builder/文件流 |
| `TraversalCancellation` / `RunLog` | 前者处理停止和 NX 调用边界；后者只记录日志，不决定是否取消 |

事务规范：

1. 构造函数只验证/保存自身选项，不打开源部件或创建文件。`Process` 只处理传入目标，不递归、不自行筛选/去重、不创建或关闭公共上下文、不激活/关闭源部件。
2. 读取 `context.Id/Name/TargetPart` 等输入；通过 `PrepareImageView`、`MeasureBoundingBox` 复用公共能力。显示服务按需初始化，所以只导出属性的事务不需要单建模视口，也不会为截图而隐藏分支。
3. 事务内较长循环调用 `context.CheckCancellation()`；普通 NX 工作可用 `context.NxCall(stage, action)`。自己的 Builder 等资源用 `finally` 清理，清理不要经过会因取消而跳过的工作入口。不要吞掉异常后冒充成功。
4. 主循环在每个事务返回/抛错后恢复经公共服务改变的画面，检查原 work/display/work component；同一目标的显示范围和加载保留给后续事务，最后统一恢复、卸载。恢复失败立即停止并保留部件。
5. `Finish()` 在已接受的一轮任务结束时调用一次，包括取消、后续事务未执行、启动检查失败的情况；用于关闭整个运行期间的文件流/输出汇总。跨目标仅保存字符串、数值等结果，不缓存待卸载的 NX 对象或 `TransactionContext`。同一实例不能重复登记。

默认 `Run(config)` 仍按 UI 配置登记 XYZ、截图。也可显式登记其他事务，不改遍历代码；此重载只读取公共筛选/输出/深度配置，各事务自行验证其构造参数：

```csharp
public sealed class NameLogger : ITransactionHandler
{
    public void Process(TransactionContext context)
    {
        context.Log(context.Id + ": " + context.Name);
    }
    public void Finish() { }
}

// 在 NX 主线程中调用；每轮传入新的事务实例。
AssemblyTraverser.Run(new TraversalConfig { filePath = @"D:\output" },
    new ITransactionHandler[] {
        new NameLogger(),
        new BoundingBoxExporter("ACS"),
        new ImageExporter(new[] { ImageExporter.SnapViewType.Top })
    });
```

旧扩展需改为上述 `Process(TransactionContext)` 签名并重新编译；删除的直接 `Process(Component, Session, ...)` 重载不再作为旁路入口。原 `NxAssemblyExportContext` 现在是内部实现，事务不拥有它的生命周期。

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

运行时会出现一个“NX Traverser — 遍历控制”小窗口。点击 **停止后续遍历**（或该窗口的 X），当前 NX 调用返回后就跳过剩余视角、事务和组件；显示恢复、临时坐标系清理及部件卸载仍执行，之后窗口自动关闭。已输出文件保留。停止不会强杀 NX，也不能让正在阻塞的原生调用立即返回。

程序还检查 NX 的取消标记，将能读到的“工作进行中”取消提升为整轮停止。但官方说明 NX 可能已处理并清掉该信号，此时不能靠轮询可靠追溯；**需要停止整轮时，请使用新增的独立按钮**。外部程序仍可调用 `AssemblyTraverser.RequestStop()`。

旧配置属性 `useIsolatedImageView` 默认 false，仅保留调用兼容。指定 true 会在运行前明确拒绝；请删除旧调用中的 true 设置。合并 ZIP 时删除旧 `NxImageContext.cs`、`NxAssemblyImageContext.cs` 和 `NxPartContext.cs`，保留 `NxViewState.cs` 和新增 `NxAssemblyExportContext.cs`，避免通配编译带入废弃代码。

## 独立检查

```text
python tests/validate_dlx.py
dotnet run --project tests/CoreTests.csproj -c Release
```

第二项只需 .NET 8 SDK、不依赖 NX 或 NuGet 包，测试旋转坐标系、多实体合并、异常数据、CSV/文件名、部件释放所有权、显示恢复和实际导出器的替身控制流。它不替代 NX 2512 编译或 Teamcenter 验收。
