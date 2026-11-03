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

## 本次更新：撤除隔离截图，恢复普通原型截图

接续 `053a2fc`。用户明确反馈隔离方案此前已试过：窗口不及时关闭会累积，图片带绿色边框和左下角编号。因此停止使用这条路线，不再修补隔离布局退出。前几次 925052/580039 的恢复尝试尚未完成实机验收，本次也不要求继续录制隔离退出 Journal。

**这是撤除不合用方案、恢复普通截图的版本，不是只读弹窗已解决的版本。** 用户已定位 `SetActiveDisplay (activate target)` 内出现提示，即使先完整加载也没有消除。本次普通截图再次使用该接口，仍可能提示；云端没有 NX 运行时，不能承诺无弹窗。

本次改动：

- 删除 `NxImageContext.cs` 及隔离视图、布局切换、退出和实例相机补偿代码。没有创建隔离窗口、裁剪绿色边框或自动确认弹窗的代码。ZIP 合并时必须删除旧 `NxImageContext.cs`，否则根目录通配编译会带入已废弃代码；先保留其中的本地改动备份，再比较处理。
- `ImageExporter.cs` 恢复普通目标原型工作视图的 `ImageExportBuilder` 截图。保留八个方向、自动适配、透明 PNG、边缘增强。图片按普通图形区域大小输出，**不再固定 1600×1200**；范围按目标原型的显示状态，须对照原项目图像验收。
- `NxViewState.cs` 从旧上下文文件提取视图快照逻辑：截图期间隐藏 WCS/三重轴，成功、异常和协作式取消后恢复各自原始开关及相机。相机恢复失败时仍尝试恢复坐标轴开关，并停止遍历。
- 截图和测量共用每个匹配原型的一次上下文激活/恢复，组合运行不再为两个事务重复切换。原有逐节点卸载、3615013 后重查会话、保留原有部件及修改标记的规则继续有效。显示/工作上下文恢复失败时停止并保留当前加载集合。
- `useIsolatedImageView` 仅保留调用兼容属性，默认 false；旧代码指定 true 会在运行前报“隔离截图已停用”，请移除 true 或改为 false。直接构造 `ImageExporter(..., true)` 同样拒绝。没有新增 DLX 控件。

### 本地本次优先验收

1. 使用本次 ZIP 的完整源码重新编译；确认旧 `NxImageContext.cs` 已移除，新 `NxViewState.cs` 在项目中。以本机 NX 2512 引用为准，不能把云端替身编译当成插件编译。
2. 从没有前版残留隔离窗口的普通建模视图开始，仅截图一个之前成功出图的部件。预期日志为 `SetActiveDisplay (activate target) → Image overlays WCS=False Triad=False → Capture normal view → Restore view → SetActiveDisplay (restore display) → Unload`。若目标已经是工作/显示部件，则无需切换。不得再出现 `CreateIsolateView`、`Capture isolated work view`、`Restore image layout`、`ChangeLayout`。
3. 检查八个方向、图像范围、透明背景、边缘增强和中文路径；检查没有隔离的绿色边框及左下角编号。记录实际像素尺寸。连续处理多个部件、重复运行两轮，确认没有新增隔离窗口；不要仅凭 PNG 文件存在判定图片内容正确。
4. 验证 WCS/三重轴原来开/关的四种组合；正常、导出失败、协作式取消后均恢复原值和原相机。若原 work 与 display 不同或 work 是重复实例中的第二个，确认恢复同一个 work occurrence 和 reference set。
5. 确认每件完成后及时卸载；保留初始完整加载/已修改/活动部件及递归祖先。对含螺栓后代的装配复测父关闭引起的失效对象处理，记录 LoadedParts 和 PrivateMB；真实释放失败仍停止。
6. 分别仅测量和组合运行，确认组合运行每个匹配原型只激活一次、只恢复一次。只读提示可能仍出现；记录其 begin 无 end 的调用、完整文字及脱敏 syslog。不要用保存、签出、清标记或全局自动确认来掩盖提示。本次验收不以“已彻底无弹窗”结项。

## 累计改动

| 文件 | 变更与本地关注点 |
|---|---|
| `ClassLibrary1.dlx`、`.gitignore` | XML 声明前空行修复，XML 格式化，移除个人绝对路径，改 OK/Cancel，DLX 纳入跟踪；保留全部 9 个控件。`NX="1946"` 保留真实生成器来源，需 2512 Styler 验证另存，不能仅把数字改成 2512 |
| `UIscripts.cs` | 已弃用 Show 改 Launch；检查 Ok 返回值；只在本次 OK 成功后保存配置；Dispose 对话框后运行；Apply 只验证，Cancel 不执行；释放 PropertyList；按 DLL 所在目录优先寻找配套 DLX |
| `TraversalConfig.cs`、`ExportUtilities.cs` | 输入验证、空白过滤、中文 ACS/WCS 映射；`maxDepth` 默认 5；CSV 引号转义/固定小数点；安全且防冲突的 Windows 文件名；旧隔离配置仅保留 false 兼容，true 明确拒绝 |
| `AssemblyTraverser.cs`、`NxGeometry.cs` | 新建每次运行目录；按需加载并检查/释放状态；检查 OpenComponentStatus；跳过抑制项；相同原型只处理一次；匹配装配后不单独导出后代；按稳定部件路径去重；截图后代不再强制完整加载；截图和测量共用每个匹配原型的一次上下文；节点 finally 清理临时部件；事务独立捕获错误；防止重入 |
| `NxPartResources.cs`、`PartLifetime.cs` | 运行前基线、活动祖先保护、逐节点卸载及释放失败停止；关闭后重查会话，避免失效包装对象；保护原有完整加载/已修改/显示/工作部件，允许卸载本次使用的原有干净部分加载部件；记录 LoadedParts/PrivateMB；独立所有权策略可回归 |
| `NxPartContext.cs` | 显示切换前先完整预加载当前目标原型，检查状态后再激活；恢复原显示部件不增加显式预加载。`641058` 仅记警告，仍验证实际加载状态；其他状态（含 `641070`）保持失败。使用 SetActiveDisplay 建立目标原型上下文；恢复原显示部件及精确工作组件，PreserveRefset；恢复失败停止遍历并保留当前加载集合。官方说明显示切换可能重置 undo 标记，不能承诺恢复撤销历史 |
| `ImageExporter.cs`、`NxViewState.cs` | 普通原型工作视图截图，不创建隔离视图；隐藏 WCS/三重轴，finally 恢复原始开关和视图。相机恢复失败停止。Builder 始终销毁；取消不计为完整截图事务；不清理源几何 |
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

此前 4.7.2 兼容选项更新只修改构建选择、产物路径和交接说明；本次普通截图回退及尚未解决的只读提示见前文。构建脚本使用 UTF-8 BOM，兼容 Windows PowerShell 5.1 对中文脚本的读取。

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
- 当前 display 与 work 不同、原 work 是重复实例中的第二个：截图、测量和组合运行必须恢复同一个 work occurrence，而不只是同一 Part。
- 输出目录不可写、CSV 被 Excel 占用、图片导出失败、加载失败时：异常有日志、Builder/CSV/加载状态释放、源部件上下文恢复。资源恢复失败应停止后续处理。
- 若仍有只读提示，先依据阶段隔离测试显示切换、加载、Orient 和临时 CSYS。站点回调或旧部件转换可能是剩余原因，不能宣称通过“代码不调用 Save”就完全排除弹窗。

## 当前已验证及未验证

- 云端 **112 项独立 C# 断言通过**：数学/文本、所有权和逐节点清理、关闭失效对象复核、显示上下文、普通截图的坐标轴隐藏/恢复、失败/取消/无输出文件处理及恢复后卸载。删除了随隔离实现一同废弃的测试，不能将数量变化解读为 NX 实机覆盖率。
- 测试使用 .NET 8.0.427 SDK 的 Roslyn 编译并执行；NX 相关代码与显式测试替身组合，生成的是假文件，不验证 Siemens API 签名、真实 PNG、窗口或弹窗。
- `python tests/validate_dlx.py`、`git diff --check` 通过；15 个插件源文件及 3 个测试源文件通过 C# 7.3 语法解析，这不包含 NX SDK 类型检查。
- 同一测试程序使用 Microsoft 官方 net472 1.0.3 引用程序集编译通过；未在 Framework 4.7.2 运行，也未编译完整 NX 插件。此前已核对默认 v4.8、显式 v4.7.2 及独立输出目录，本次没有改构建目标。
- 官方 API 依据见 `docs/NX2512_API_REVIEW.md`。**待本地**：完整插件的 NX SDK 编译、签名、DLX 加载、普通截图内容/尺寸/轴隐藏/视图恢复、连续运行窗口数、内存和 Teamcenter 提示。只读弹窗仍未解决。

## 回传并持续维护

完成后给出：下载包对应的分支/提交、NX/TC/VS 版本、构建与签名结果、DLL/DLX 的最终 SHA256、部署路径、每项验收通过/失败/未测、脱敏日志和复现步骤。

若修了代码或 DLX，把修改文件按原目录结构打包，并列出新增/修改/删除清单；同步更新本文件的“改动、验证状态、剩余工作”和 API 依据。由用户通过浏览器回传给云端 agent，再由云端 agent 核对并提交到同一 GitHub 分支。本地不需要具备 GitHub 推送能力。不要只交付本机 DLL 而不回传对应源码，也不要把 NX SDK、许可或公司模型放入回传源码包。
