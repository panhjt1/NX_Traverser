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

## 本次改动

| 文件 | 变更与本地关注点 |
|---|---|
| `ClassLibrary1.dlx`、`.gitignore` | XML 声明前空行修复，XML 格式化，移除个人绝对路径，改 OK/Cancel，DLX 纳入跟踪；保留全部 9 个控件。`NX="1946"` 保留真实生成器来源，需 2512 Styler 验证另存，不能仅把数字改成 2512 |
| `UIscripts.cs` | 已弃用 Show 改 Launch；检查 Ok 返回值；只在本次 OK 成功后保存配置；Dispose 对话框后运行；Apply 只验证，Cancel 不执行；释放 PropertyList；按 DLL 所在目录优先寻找配套 DLX |
| `TraversalConfig.cs`、`ExportUtilities.cs` | 输入验证、空白过滤、中文 ACS/WCS 映射；`maxDepth` 默认 5；CSV 引号转义/固定小数点；安全且防冲突的 Windows 文件名 |
| `AssemblyTraverser.cs`、`NxGeometry.cs` | 新建每次运行目录；按需加载并检查/释放状态；检查 OpenComponentStatus；跳过抑制项；相同原型只处理一次；匹配装配后不单独导出后代；按稳定部件路径去重；移除截图前强制完整加载；节点 finally 清理临时部件；事务独立捕获错误；防止重入 |
| `NxPartResources.cs`、`PartLifetime.cs` | 新增运行前基线、活动祖先保护、逐节点卸载及释放失败停止；保护原有完整加载/已修改/显示/工作部件，允许卸载本次使用的原有干净部分加载部件；记录 LoadedParts/PrivateMB；独立所有权策略可回归 |
| `NxPartContext.cs` | `641058` 仅记警告，仍验证实际加载状态；其他状态（含 `641070`）保持失败。使用 SetActiveDisplay 建立目标原型上下文；恢复原显示部件及精确工作组件，PreserveRefset；恢复失败停止遍历。官方说明显示切换可能重置 undo 标记，不能承诺恢复撤销历史 |
| `ImageExporter.cs` | 不再调用 CollectBodyTags/强制完整加载后代；改遍历部分加载显示数据，等显示操作完成再截图。删除无效 UFSession.UI/AskSuppressDialogs 和反射常量；删除 WCS 可见性写入、固定三轴设置、源几何清理；保留已有 WCS/三轴可见性，恢复原视图；Builder 在 finally 销毁；恢复失败停止遍历；中途取消不计为完整截图事务 |
| `BoundingBoxExporter.cs`、`BoundingBoxMath.cs` | 官方确认 Tag.Null 表示当前 WCS，不是 ACS。ACS 使用临时单位 CSYS；WCS 直接查询该方向的精确盒；按 UF 方向向量聚合八角点；装配后代使用 occurrence tag；临时对象 finally 清理；失败时不写偏小的尺寸 |
| `RunLog.cs` | `Traversal.log` 同步 Listing Window，含阶段、只读/修改状态、NX 错误码；检测加载后新增修改标记；不清除修改标记 |
| `NX_Traverser.csproj`、`scripts/build.ps1` | 默认 net48 / x64，新增显式 v4.7.2 兼容编译参数；使用当前安装的 NXBIN/managed；Siemens 引用 Copy Local=false；嵌入本机 NXSigningResource.res；输出按框架隔离的 DLL/DLX、构建日志和 SHA256 |
| `tests/` | 独立 C# 核心回归和 XML/控件一致性验证；新增 PartResourceTests，使用明确的测试替身验证真实资源管理器的清理控制流，测试替身不是 NX SDK，不验证 Siemens ABI/API 或弹窗 |

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

## 本次内存与只读日志修复（2026-11-03，接续 e3b6546）

用户实测日志显示：截图前及其螺栓子件的 `LoadThisPartFully` 返回 `641058`；旧代码把所有 load status 当成失败，且整轮不卸载。`641070` 是另一种组件版本错误，不能忽略。最后一次弹窗的具体来源尚未确认，不能把同时间出现的日志当作已证实的唯一原因。

本次实施：

1. 仅截图不再显式调用完整加载/精确实体收集。仍递归加载未抑制后代的显示数据并检查 `PartLoadState`；测量仍要求 `IsFullyLoaded`，任何其他加载错误仍使该事务失败。`641058` 不再直接判失败，若实际加载状态不满足要求仍失败。
2. 每个遍历节点在 `finally` 中，先恢复视图和 work/display，再释放本次新打开的部件，包括失败操作带入的依赖。初始干净的部分/最小加载部件，若被本次触碰或自动提升加载，也允许卸载；**这些原有部件运行后可能处于未加载状态**，不会自动重载以免重新占用内存。
3. 保留初始完整加载部件、初始已修改部件、全部原始显示窗口、原工作部件与工作组件祖先；父递归帧仍在使用的原型有引用计数保护。共享原型不会因一个子帧退出而关闭。去重使用路径，不再依赖卸载重载会改变的 tag。
4. 使用 `BasePart.Close(False, CloseModified, null)` **只关单个部件**，丢弃该临时部件本次加载/显示造成的内存变化，不调用保存，不使用整树关闭或 CloseAll。关闭后核查是否仍在 Parts 集合；失败即停止继续加载，并在最终清理重试。不会删除全部用户 undo 来强行回收内存。
5. `[Unload begin/done]` 和 `[Memory] LoadedParts/Closed/PrivateMB` 用于验证确实卸载；`[WARN retained modified]` 说明保留的初始部件新增了修改标记，最后返回 NX UI 时仍可能只读提示。没有添加全局对话框屏蔽，也没有验证到保证无弹窗的公开开关。

单个匹配装配仍需容纳其显示数据；精确测量还需容纳其完整几何。NX 内部缓存/撤销记录不一定立即归还进程内存，不能把 PrivateMB 不立刻下降直接判成没关闭，也不能仅凭 LoadedParts 下降就宣称没有内存问题。

### 本地优先验收本次修复

在独立测试会话、相同起始加载状态下使用用户原来的过滤条件：

- 先“仅截图”，再“仅测量”，最后组合；截图日志不应出现由本工具显式发起的 `[Load geometry]`，图片应完整，视角/参考集/隐藏状态符合预期。检查日志中那类带螺栓的匹配装配。
- 对实际返回 `641058` 的样例验证：状态足够时继续导出；不足时仍失败。包含 `641058` + 另一错误的混合状态不能整体放行。`641070` 仍报错，不自动换成其他修订版本。
- 用大量不同原型和跨分支重复原型，检查每次 `[Unload done]`、稳定的 LoadedParts 趋势、PrivateMB 峰值及完成后的内存；已导出原型再次出现应跳过，即使重载后 tag 不同。至少连续跑两轮，并与旧版同场景比较。
- 覆盖父子共享原型、原工作组件位于子装配、多个显示窗口、初始已修改部件、初始完整加载部件、初始干净部分加载部件。前五类受保护；最后一类在被本次使用后允许卸载。关闭不能删除树节点，未访问的部分加载部件应保留。
- 人为制造加载失败、输出不可写、导出失败和协作式取消：临时部件仍应被释放、原始工作/显示上下文恢复。关闭失败必须记录 `[STOP]` 并停止继续加载，不能继续把整树装进内存。
- 注意任务结束返回界面后的最后一次弹窗。记录出现时最后一条日志、完整弹窗文本和 NX syslog；若有 `[WARN retained modified]`，记录对应部件的修改信息。分别比较仅截图/仅测量，确认是否仍由加载升级、视图、临时 CSYS 或站点回调触发。**不以清除 IsModified、强行签出或保存已发布部件作为通过标准。**

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
- 八个视角 PNG 方向正确、透明背景和中文路径有效；截图保持原 WCS/三轴可见性；执行结束视图矩阵、平移、比例恢复。
- 测量结果 CSV 名称包含逗号/双引号/换行时可正确读回；在小数逗号地区仍使用小数点；每次运行独立目录，保留前次结果。

### Teamcenter 与故障恢复（重点）

- 分别用可写未发布、已发布只读、他人签出、无写权限但可读的样例，测试：仅测量、仅截图、两者同时开启。
- 每组记录是否出现“该部件是只读的”，以及出现时日志最后的阶段、完整文字、NX 错误号和 syslog；同时比较运行前后 IsModified、显示/工作组件、参考集、WCS 和视图。
- 确认无保存/签出/修改 ACL/清除修改标记。逐项核查下面的卸载保护规则；原有干净部分/最小加载部件允许被卸载，其树节点应仍存在且可再加载，不是删除组件。
- 当前 display 与 work 不同、原 work 是重复实例中的第二个：完成及失败后必须恢复同一个 work occurrence，而不只是同一 Part。
- 输出目录不可写、CSV 被 Excel 占用、图片导出失败、加载失败时：异常有日志、Builder/CSV/加载状态释放、源部件上下文恢复。资源恢复失败应停止后续处理。
- 若仍有只读提示，先依据阶段隔离测试显示切换、加载、Orient 和临时 CSYS。站点回调或旧部件转换可能是剩余原因，不能宣称通过“代码不调用 Save”就完全排除弹窗。

## 当前已验证及未验证

- 云端 **54 项独立 C# 断言通过**：原有数学/文本 28 项，部件所有权策略 11 项，资源管理器控制流 15 项。后两组覆盖共享祖先、原有编辑、部分加载提升、加载异常依赖、工作部件保护、关闭拒绝和关闭异常。使用 .NET 8.0.427 SDK 的 Roslyn 编译并执行；资源管理器测试使用 `tests/PartResourceTests.cs` 中的 NX 测试替身，不代表 NX SDK 编译或原生卸载验证。
- `python tests/validate_dlx.py` 通过；`git diff --check` 通过。
- 对比旧 DLX 解析树，除文件标题、日期尾部空白、OK/Cancel 导航外，原属性均保留。14 个插件源文件及 2 个测试源文件通过 C# 7.3 语法解析；这不包含 NX SDK 类型检查。
- 已读取并核对官方 2512 API 链接，见 `docs/NX2512_API_REVIEW.md`。
- 4.7.2 选项更新：使用 Microsoft 官方 `Microsoft.NETFramework.ReferenceAssemblies.net472` 1.0.3 的 mscorlib/System/System.Core，按 C# 7.3 编译通过独立核心测试程序及资源管理器测试替身构建；未在 Framework 4.7.2 运行时执行该程序，也未编译完整 NX 插件。MSBuild 项目求值确认默认 v4.8、显式 v4.7.2 及相互独立的输出/中间目录。
- **待本地**：NX SDK 编译/链接、签名、原生 DLX 打开、视图恢复精度、PNG、临时 CSYS 的只读行为、Teamcenter 全部实测。这里没有 NX 运行时，不能将离线检查替代这些结果。

## 回传并持续维护

完成后给出：下载包对应的分支/提交、NX/TC/VS 版本、构建与签名结果、DLL/DLX 的最终 SHA256、部署路径、每项验收通过/失败/未测、脱敏日志和复现步骤。

若修了代码或 DLX，把修改文件按原目录结构打包，并列出新增/修改/删除清单；同步更新本文件的“改动、验证状态、剩余工作”和 API 依据。由用户通过浏览器回传给云端 agent，再由云端 agent 核对并提交到同一 GitHub 分支。本地不需要具备 GitHub 推送能力。不要只交付本机 DLL 而不回传对应源码，也不要把 NX SDK、许可或公司模型放入回传源码包。

本次交付方式更新只调整文档，没有修改插件代码、DLX 或构建逻辑；此前验证状态保持原记录。
