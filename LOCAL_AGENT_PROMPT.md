# 本地 agent：NX 2512 编译、验收与部署提示词

更新时间：2026-11-03。目标分支：`fix/nx2512-teamcenter-readonly`；修改基线：`2470a54`。这是本仓库的持续交接文档，后续每次代码、DLX 或构建配置变更都必须同步维护。

## 任务与边界

你在 Windows + Siemens NX 2512 + Teamcenter 集成环境工作。请完成下面的 SDK 编译、签名、实机验证，再部署测试版本。先阅读 [官方 API 核对与审查](docs/NX2512_API_REVIEW.md)。云端已经修改源码和资产，但**没有声称 NX 编译或 Teamcenter 无弹窗验收通过**。

不要为了通过测试而签出源部件、保存修改、修改发布状态/ACL、清除 IsModified、删除用户部件、屏蔽全部 NX 对话框。不要把 1946 的 DLL 混入 2512 引用。保留本地已有源码修改、项目配置和验证记录，不用新压缩包直接覆盖旧目录。

## 通过浏览器下载源码 ZIP（本地不要求 Git 联网）

用户的浏览器与本地 agent 网络环境隔离，使用浏览器下载源代码压缩包，再交给本地 agent。不要要求本地执行 git clone/fetch/pull，也不要假设解压目录包含 `.git`。

1. 云端 agent 推送 `fix/nx2512-teamcenter-readonly` 分支并更新本文档，用户自行在 GitHub 页面下载该分支 ZIP；不需要额外提供下载链接。不要拿 main 的源码验收本次修复。
2. 记录下载时分支页面上的提交号；分支 ZIP 会随更新变化，仅凭压缩包文件名不能确定版本。
3. 将 ZIP 传入本地工作环境，解压到新的独立目录。若旧目录有本地修复，先备份并比较差异，再合并；不要直接覆盖。下载和传输不依赖 OpenAI 云存储。
4. 在含 `NX_Traverser.csproj` 的解压目录中执行下方构建命令。构建脚本不调用 Git，也不自动下载依赖；NX 引用、MSBuild 和相应 Targeting Pack 需由本机提供。
5. 如需额外开发包，通过可联网浏览器取得安装包/官方引用包后按公司允许的方式传入本地，不假设本地 agent 能执行在线安装或 NuGet 还原。

## 本次更新：580039 后核验恢复状态，不能视为实机修复完成

接续 `e5a7cc2`。用户最新日志显示：隔离后有新的 CurrentLayout，ActiveViews 同时包含隔离视图和原视图；ChangeLayout 返回 580039「使用了一个已删除或无效的类号」。旧代码立即停止，未取得调用后的布局/视图状态。用户随后明确确认：报错后画面已回到原装配。因此界面切换已经发生，但不能仅凭视觉反馈认定所有 NX 对象状态均恢复；是否为临时对象清理阶段报错仍未证实。

本次改动：

- 恢复过程中按原 tag 从 Layouts.ToArray / Views.ToArray 重新取得对象，不跨原生切换继续使用旧布局/视图包装对象；视图快照仍保存原矩阵、位置、比例和 WCS/三重轴开关。
- ChangeLayout 成功或失败都输出 `[Image state] after ChangeLayout (including error)`，异常退出另有 `restore failed`。CurrentLayout、WorkView、ActiveViews、DisclosedViews 分别查询；某一项失败会写 NXError，不丢掉其他可读状态。日志不再读取旧布局对象的 DisplayStatus。
- 只针对 580039 增加有条件恢复：原布局确已成为 CurrentLayout，唯一 ActiveViews 是原视图，且隔离 tag 不在 GetDisclosedModelViews 返回列表中，才继续恢复原工作视图和显示状态；刷新显示后再次核对 display/work、布局、活动视图、工作视图和展开状态。
- 全部核验通过才输出 `[Image restore verified]` 并允许逐节点卸载。580039 仍记录，调用的 Completed=False 不会被改写；不完整状态、其他错误、读取状态失败或后续恢复失败仍停止并保留部件。不增加自动重试、不重新 Open、不激活只读原型、不强删临时窗口或清空全部撤销记录。
- **这不是新的、已证实的退出接口**。如果 ChangeLayout 实际没有退出隔离，这版仍会停止，只会给出更完整的诊断。先完成下面单件验证及必要的原生 Journal 采集，再决定后续退出实现，不以测试替身通过代替实机证据。

### 本地本次优先验收

1. 重新编译，在已退出残留隔离的普通单视口中，仅截图同一单件。完整保留 captured、isolated、before restore、after ChangeLayout (including error)、after restore / restore failed 各行，并记录报错后画面是否已返回原装配。
2. 只有出现 `Image restore verified`（580039 恢复分支）及 `Image context restored → Unload done`，才视为该次上下文恢复和清理完成。正常无异常分支不需要 verified 行。比较原视图/布局 tag、图片范围、隐藏坐标轴、恢复开关及最终 LoadedParts。
3. 若隔离仍显示或状态不符，不继续大树测试。由本地 agent 协助在 NX 2512 测试会话中录制 C# Journal：开始录制 → 对一个组件手动执行相同的隔离操作 → 用正常 UI 关闭该隔离窗口/退出隔离 → 停止录制。截取创建和退出附近的调用及必要对象定义，脱敏后交回；无需模型文件。
4. 如果 Journal 没记录退出动作，如实注明录制为空或操作不支持录制，并记录实际 UI 操作名称及 NX maintenance build；不要自行猜菜单命令、使用反射或自动确认弹窗。结合新增状态日志与本机 SDK/Siemens 支持确认退出方式。
5. 单件恢复、图像和卸载都确认后再连续两轮，最后扩大到多件。源部件保存/签出、清修改标记不属于本次处理方案。

## 前次更新：隐藏坐标轴、隔离截图和卸载失效对象修复

本次接续 `4983001`。最新实测已定位：先完整加载并返回 `641058` 后，`SetActiveDisplay (activate target)` 内仍出现弹窗。此前“预加载可能解决”的假设未成立。另一次关闭已完成后出现 `3615013`，旧代码仍访问了被关闭连带失效的包装对象。

- **恢复原功能**：截图默认隐藏 WCS、三重轴，仍保留八个方向、自动适配、透明 PNG 和边缘增强。使用 `View.WcsVisibility/TriadVisibility`，结束/异常/协作式取消恢复各自原始开关；不强制恢复为显示，也不修改全局三轴偏好或源 `part.WCS.Visibility`。原视图未改变时不额外写回矩阵。
- **截图默认路径改变**：`useIsolatedImageView=true`，在原工作/显示装配内创建临时隔离视图，不经过 `NxPartContext`，不显式完整加载并激活目标原型。只用工作视图生成 1600×1200 PNG；后代仍按部分加载准备显示数据。NX 内部是否隐式升级加载仍以本机记录为准。
- **图片范围需对照**：采用首次匹配实例的当前参考集、布置和隐藏状态。补偿实例旋转，保持原型八个视角方向；不等同于脱离装配显示整个原型。重复原型仍只输出一次。镜像、剪切、非均匀缩放不支持，明确报错。
- **恢复边界**：要求工作部件=显示部件，普通单建模视口；运行前退出已有隔离、爆炸、制图、分屏视图。使用 ChangeLayout 恢复原布局，然后按需替换临时视图，恢复工作视图/展开状态/矩阵/可见性并核对。此组合调用尚未在真实 NX 中验证；失败会停止并保留当前加载集合，防止继续关闭仍被视图引用的部件。
- **测量边界**：精确测量仍使用原型上下文，可能弹只读提示；仅截图与组合运行必须分开验收。`useIsolatedImageView=false` 仅为显式对比旧路径的代码选项，没有新增 DLX 控件，也不会失败后自动切回旧路径。
- **卸载修复**：跨关闭操作只保存 tag/名称，每次重新查询当前部件。父部件关闭使其他对象失效时，已消失的对象计为已释放；仅当新枚举确认目标消失才容忍 3615013，目标还在会话中或其他关闭错误仍停止。原有部件保护和逐节点清理继续有效。

### 本地先做的验收

1. 全量引用本机 NX 2512 SDK 编译。先在独立测试会话中，仅截图一个此前会提示的部件，八个方向，普通单视口且顶层装配同时为工作/显示部件。
2. 日志应有 `CreateIsolateView → Image overlays WCS=False Triad=False → Capture isolated work view → Restore image layout → Image context restored → Unload`；仅截图不应出现目标的 `SetActiveDisplay` 或显式 `LoadThisPartFully`。根装配自身匹配时直接使用原工作视图，不创建隔离视图。
3. 确认图片确实只包含目标及其所需后代，无 WCS/三重轴，透明背景、边缘增强、1600×1200、中文路径正确；确认没有漏体、没有把整棵装配截进去。分别比较旋转/平移/嵌套实例、非默认参考集及隐藏件。测量范围与实例图片范围可能不同。
4. 逐个验证 WCS/三重轴原来开/关的四种组合；成功、输出错误、中途取消后应恢复原值、视图矩阵/比例/布局、展开状态和原工作/显示部件。连续两轮确认隔离已退出且不会累积临时视图。若原生退出失败，先记录日志和界面状态，不直接跑整树。
5. 单件通过后，再跑含螺栓后代的匹配装配及大树：确认父关闭带来的失效对象不再误报 STOP；真实关闭拒绝仍停止。记录 LoadedParts、PrivateMB 峰值及两轮结束状态；不能只靠内存数值立即下降判断关闭。
6. 再单独测量及组合运行。仍有弹窗时记录最后一个有 begin 无 end 的调用、完整弹窗文字和脱敏 syslog。不要声称这版已保证所有加载/站点回调无弹窗。

## 累计改动

| 文件 | 变更与本地关注点 |
|---|---|
| `ClassLibrary1.dlx`、`.gitignore` | XML 声明前空行修复，XML 格式化，移除个人绝对路径，改 OK/Cancel，DLX 纳入跟踪；保留全部 9 个控件。`NX="1946"` 保留真实生成器来源，需 2512 Styler 验证另存，不能仅把数字改成 2512 |
| `UIscripts.cs` | 已弃用 Show 改 Launch；检查 Ok 返回值；只在本次 OK 成功后保存配置；Dispose 对话框后运行；Apply 只验证，Cancel 不执行；释放 PropertyList；按 DLL 所在目录优先寻找配套 DLX |
| `TraversalConfig.cs`、`ExportUtilities.cs` | 输入验证、空白过滤、中文 ACS/WCS 映射；`maxDepth` 默认 5；CSV 引号转义/固定小数点；安全且防冲突的 Windows 文件名；新增隔离截图默认配置和实例旋转补偿 |
| `AssemblyTraverser.cs`、`NxGeometry.cs` | 新建每次运行目录；按需加载并检查/释放状态；检查 OpenComponentStatus；跳过抑制项；相同原型只处理一次；匹配装配后不单独导出后代；按稳定部件路径去重；截图后代不再强制完整加载；按事务选择原型上下文，默认截图不切换；节点 finally 清理临时部件；事务独立捕获错误；防止重入 |
| `NxPartResources.cs`、`PartLifetime.cs` | 运行前基线、活动祖先保护、逐节点卸载及释放失败停止；关闭后重查会话，避免失效包装对象；保护原有完整加载/已修改/显示/工作部件，允许卸载本次使用的原有干净部分加载部件；记录 LoadedParts/PrivateMB；独立所有权策略可回归 |
| `NxPartContext.cs` | 显示切换前先完整预加载当前目标原型，检查状态后再激活；恢复原显示部件不增加显式预加载。`641058` 仅记警告，仍验证实际加载状态；其他状态（含 `641070`）保持失败。使用 SetActiveDisplay 建立目标原型上下文；恢复原显示部件及精确工作组件，PreserveRefset；恢复失败停止遍历。官方说明显示切换可能重置 undo 标记，不能承诺恢复撤销历史 |
| `ImageExporter.cs`、`NxImageContext.cs` | 默认临时隔离截图，使用工作视图图像捕获；恢复 WCS/三重轴隐藏功能，finally 恢复原始开关和视图。隔离布局恢复失败即停止并保留当前加载集合。Builder 始终销毁；取消不计为完整截图事务；不清理源几何 |
| `BoundingBoxExporter.cs`、`BoundingBoxMath.cs` | 官方确认 Tag.Null 表示当前 WCS，不是 ACS。ACS 使用临时单位 CSYS；WCS 直接查询该方向的精确盒；按 UF 方向向量聚合八角点；装配后代使用 occurrence tag；临时对象 finally 清理；失败时不写偏小的尺寸 |
| `RunLog.cs` | 新增成对的 NX 调用 begin/end、编号、完成状态和耗时；`Traversal.log` 同步 Listing Window，含阶段、只读/修改状态、NX 错误码；检测加载后新增修改标记；不清除修改标记 |
| `NX_Traverser.csproj`、`scripts/build.ps1` | 默认 net48 / x64，新增显式 v4.7.2 兼容编译参数；使用当前安装的 NXBIN/managed；Siemens 引用 Copy Local=false；嵌入本机 NXSigningResource.res；输出按框架隔离的 DLL/DLX、构建日志和 SHA256 |
| `tests/` | 独立 C# 核心回归和 XML/控件一致性验证；PartResourceTests 和 ImageViewTests 使用明确的测试替身验证资源管理器、显示上下文和截图恢复控制流；不是 NX SDK，不验证 Siemens ABI/API、真实 PNG 或弹窗 |

## 构建与签名

1. 记录实际 NX 2512 maintenance build、Teamcenter 版本、NX 启动方式、VS/MSBuild 版本。确认运行中的 NX 与引用 DLL 来自同一 2512 安装。
2. 默认使用 Visual Studio 2022（或支持 C# 7.3、MSBuild 15.8+ 的 VS / Build Tools）和 **.NET Framework 4.8 Developer/Targeting Pack**。若显式试编译 v4.7.2，则需要对应 4.7.2 开发包。本插件不使用 net8/managed_core。只有运行库并不代表安装了开发引用包。
3. 使用 **NX 2512 Block UI Styler** 打开 `ClassLibrary1.dlx`。确认中文标签、9 个 ID、八个英文视角条目、多选视角和 OK/Cancel。若工具要求升级另存，执行后将修改的 DLX 纳入回传文件；不要生成模板覆盖 `UIscripts.cs`。XML 能解析不代表原生 Styler 验证通过。
4. 编译，安装路径以本机实际位置为准：

```powershell
.\scripts\build.ps1 -NXInstallDir 'C:\Program Files\Siemens\NX2512' -Configuration Release
```

默认输出为 `bin/Release/v4.8/NX_Traverser.dll` 与 `ClassLibrary1.dlx`，以及 PDB；v4.7.2 输出在 `bin/Release/v4.7.2/`，避免两种目标产物混用。`artifacts/build-Release-v4.8.binlog`（或 v4.7.2 对应日志）留在本地。目录内不应有任何 `NXOpen*.dll` 或 `ManagedLoader.dll`。记录全部警告，特别是 Obsolete、引用版本和平台警告；发现 API 不符时先查本机官方 XML 文档/2512 .NET Reference，再修代码，不使用反射猜测补丁。

5. 对无 NX Open Author 许可的使用者，应按站点授权方式签名。官方签名资源已加入项目，用 2512 安装中的工具（先在本机检查帮助/参数）：

```powershell
$nxInstall = 'C:\Program Files\Siemens\NX2512'
$plugin = '.\bin\Release\v4.8\NX_Traverser.dll' # 若验证的是 v4.7.2，请改为对应目录
& "$nxInstall\NXBIN\SignDotNet.exe" $plugin
& "$nxInstall\NXBIN\SignDotNet.exe" -verify $plugin
```

签名会改变 DLL，**签名后**再次记录 SHA256。不要将 Siemens DLL、资源文件、许可或公司部件提交到公开仓库。
6. 部署到站点测试用自定义 `application` 目录，DLL/DLX 必须配套。通过 NX 的“文件 → 执行 → NX Open/Designcenter Open”启动 DLL，入口 `ClassLibrary1.Main`；它不是可单独播放的单文件 Journal。若采用现有菜单启动方式，先验证对应卸载策略；当前 `Immediately` 用于手动执行。

## 原工程目标 4.7.2 的反馈（2026-11-03 更新）

用户已澄清：**尚未发生编译错误**，只是本地原始 csproj 写的是 4.7.2，过去按此目标编译成功；这不证明机器不能编译 4.8，也不证明原引用就是 NX 2512。

默认保留 NX 2512 官方的 Framework 4.8 基线。为复用旧工程目标，新增明确的兼容试编译命令：

```powershell
.\scripts\build.ps1 -NXInstallDir 'C:\Program Files\Siemens\NX2512' -TargetFrameworkVersion v4.7.2
```

也可直接用 MSBuild 的 `/p:TargetFrameworkVersion=v4.7.2`。两种目标都必须引用**当前 NX 2512 安装中的 NXBIN/managed**，不能拿 1946 库来换取编译成功。脚本将 MSB3274/MSB3275 视为错误，不忽略高版本框架依赖。

本地处理顺序：

1. 记录实际使用的 NXOpen.dll 路径/文件版本、MSBuild/C# 编译器版本、已安装的 Targeting Pack，以及采用的目标版本。此前“4.7.2 编译成功”是哪套 NX 引用也要确认。
2. 可按上述 v4.7.2 命令试编译。若出现 `MSB3274/MSB3275` 或 `CS1705` 等更高版本引用错误，保留报错，使用默认 v4.8 配置并补齐 4.8 开发引用包；不要关闭兼容性检查。
3. 若报 `MSB3644`，才说明指定版本的开发引用包缺失。可安装相应 Developer/Targeting Pack；无管理员权限时，可按 Microsoft 文档采用官方 `Microsoft.NETFramework.ReferenceAssemblies` 包提供编译引用，并记录这个本地构建调整。不把只装了 runtime 当作已装 targeting pack。
4. 若旧编译器报语言版本错误，检查 C# 7.3/MSBuild 工具链；这与 csproj 目标 4.7.2 是不同问题。
5. 即使 v4.7.2 编译成功，DLL 仍在 NX 宿主使用的运行时中执行；编译目标不能降低 NX 2512 的运行环境要求。签名和 Teamcenter 回归仍按本文件执行。

此前 4.7.2 兼容选项更新只修改构建选择、产物路径和交接说明；本次内存/只读修复另见下节。构建脚本使用 UTF-8 BOM，兼容 Windows PowerShell 5.1 对中文脚本的读取。

## 保留的卸载及诊断规则

- `641058` 只读加载修改警告可继续，但必须检查实际加载状态；包含其他错误的混合状态不能整体放行。`641070` 为组件版本错误，不能自动切换修订版。
- 保护初始完整加载/已修改部件、全部原显示窗口、工作部件与工作组件祖先，以及活动递归祖先。初始干净部分/最小加载部件被本次使用或自动提升后可卸载；未访问的部分加载部件保留。
- `BasePart.Close(False, CloseModified, null)` 只关闭单个任务临时部件，不保存；正常、失败、取消后均清理。树节点不能被删除，卸载后应可再次加载。重复原型使用路径去重，不能依赖重载会改变的 tag。
- 单个匹配装配仍需容纳显示数据；精确测量还需容纳完整几何。NX 缓存/撤销记录可能保留内存，不删除用户全部 undo，不承诺进程内存立即下降。
- `[NX call begin/end]` 的 Completed 只表示调用与检查完成，ElapsedMs 不足以判断是否弹窗。`[WARN retained modified]` 表示保留部件出现修改标记，仍需本地核查，不清标记。
- 没有设置 `DisplayAlertsOnWarnings` 来冒充只读通知开关，也没有接入要求 Automated Testing Studio 许可的 MessageBoxTester。

## 验证步骤与预期

先用**独立测试会话**，记录运行前 display/work part、work component、reference set、模型修改标记、WCS 和视图状态。不要在带有重要未保存编辑的生产会话直接验证显示切换。

### 离线检查

```powershell
python .\tests\validate_dlx.py
dotnet run --project .\tests\CoreTests.csproj -c Release
```

第二条需要 .NET 8 SDK，仅用于独立回归程序，与插件所选 Framework 编译目标独立，也不需要 NuGet 包。没有 Python/SDK 时可先完成 NX 编译，但应在交接结果中记录未运行项。

### 界面

- 开启/关闭截图与测量，对应控件启用状态正确；空路径、无功能、启用截图但未选视角，均拒绝提交。
- 输入多行含空白的名称/ID 模式，按 OR 匹配；无过滤时导出叶子。
- 选择“工作坐标系”，CSV 确实显示 WCS。
- 取消、点 X、先运行一次再重新打开后取消，都不能产生新运行目录或重复任务。
- 在 Styler 中确认只有 OK/Cancel，OK 后对话框关闭再开始遍历。

### 几何和图片

- 原生可写长方体 10×20×30：ACS 输出该尺寸；将 WCS 旋转 30°/45°，用 NX 测量作对照；要比较实际 WCS 对齐盒，不是期望仍保持 ACS 尺寸。
- 长条实体轴线与旋转 WCS 对齐：WCS 结果应回到长条真实长宽，不能输出旋转 ACS 外盒的高估值。
- 多实体、偏离原点超过 1e6 的部件：不能静默漏体；CSV 的单位明确区分英寸/毫米。
- 子装配中放置同一原型两次、带旋转/平移/嵌套和抑制组件：匹配子装配时尺寸包含有效后代的实际放置；单独匹配重复零件时每原型一份输出。
- 部分/最小加载组件应自动加载；无法加载、缺少权限、参考集未包含实体时记录原因，不写误导性的部分包围盒。检查异常后后续组件仍能处理。
- 八个视角 PNG 方向正确、透明背景和中文路径有效；截图隐藏 WCS/三重轴；执行结束恢复它们各自原始开关以及视图矩阵、平移、比例。
- 测量结果 CSV 名称包含逗号/双引号/换行时可正确读回；在小数逗号地区仍使用小数点；每次运行独立目录，保留前次结果。

### Teamcenter 与故障恢复（重点）

- 分别用可写未发布、已发布只读、他人签出、无写权限但可读的样例，测试：仅测量、仅截图、两者同时开启。
- 每组记录是否出现“该部件是只读的”，以及出现时日志最后的阶段、完整文字、NX 错误号和 syslog；同时比较运行前后 IsModified、显示/工作组件、参考集、WCS 和视图。
- 确认无保存/签出/修改 ACL/清除修改标记。逐项核查下面的卸载保护规则；原有干净部分/最小加载部件允许被卸载，其树节点应仍存在且可再加载，不是删除组件。
- 当前 display 与 work 不同、原 work 是重复实例中的第二个：默认隔离截图应明确拒绝并停止；测量/显式旧路径必须恢复同一个 work occurrence，而不只是同一 Part。
- 输出目录不可写、CSV 被 Excel 占用、图片导出失败、加载失败时：异常有日志、Builder/CSV/加载状态释放、源部件上下文恢复。资源恢复失败应停止后续处理。
- 若仍有只读提示，先依据阶段隔离测试显示切换、加载、Orient 和临时 CSYS。站点回调或旧部件转换可能是剩余原因，不能宣称通过“代码不调用 Save”就完全排除弹窗。

## 当前已验证及未验证

- 云端 **136 项独立 C# 断言通过**：数学/文本和所有权策略、真实清理控制流（含父关闭使子包装对象失效、3615013 目标消失/仍在会话两种分支）、显示上下文、实际截图代码的隐藏/恢复/取消/失败控制流，以及实例旋转补偿；新增双活动视图、580039 完全/部分恢复、其他错误和残留 disclosed view 的分支。使用 .NET 8.0.427 SDK Roslyn 编译执行；NX 相关测试通过显式测试替身运行，输出的是假文件，不是 NX 渲染验证。
- `python tests/validate_dlx.py` 通过；`git diff --check` 通过。
- 对比旧 DLX 解析树，除文件标题、日期尾部空白、OK/Cancel 导航外，原属性均保留。15 个插件源文件及 3 个测试源文件通过 C# 7.3 语法解析；这不包含 NX SDK 类型检查。
- 已读取并核对官方 2512 API 链接，见 `docs/NX2512_API_REVIEW.md`。
- 4.7.2 选项更新：使用 Microsoft 官方 `Microsoft.NETFramework.ReferenceAssemblies.net472` 1.0.3 的 mscorlib/System/System.Core，按 C# 7.3 编译通过独立核心测试程序及资源管理器/显示上下文/截图测试替身构建；未在 Framework 4.7.2 运行时执行该程序，也未编译完整 NX 插件。MSBuild 项目求值确认默认 v4.8、显式 v4.7.2 及相互独立的输出/中间目录。
- **待本地**：NX SDK 编译/链接、签名、原生 DLX 打开、隔离视图创建/退出、视图恢复精度、PNG/轴隐藏、临时 CSYS 的只读行为、Teamcenter 全部实测。这里没有 NX 运行时，不能将离线检查替代这些结果。

## 回传并持续维护

完成后给出：下载包对应的分支/提交、NX/TC/VS 版本、构建与签名结果、DLL/DLX 的最终 SHA256、部署路径、每项验收通过/失败/未测、脱敏日志和复现步骤。

若修了代码或 DLX，把修改文件按原目录结构打包，并列出新增/修改/删除清单；同步更新本文件的“改动、验证状态、剩余工作”和 API 依据。由用户通过浏览器回传给云端 agent，再由云端 agent 核对并提交到同一 GitHub 分支。本地不需要具备 GitHub 推送能力。不要只交付本机 DLL 而不回传对应源码，也不要把 NX SDK、许可或公司模型放入回传源码包。
