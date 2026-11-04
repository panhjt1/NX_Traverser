# NX 2512 官方 API 核对与审查记录

核对日期：2026-11-04。本次基线：`14501c9`。NX API 核对以 **Siemens 官方 2512 文档**为依据，不把旧版社区示例视为新版本 API；用户运行证据和 Microsoft 构建资料另行注明。官方文档浏览器页面动态加载，本文链接到同一文档的正文或参考类。

## 本次：临时测量对象删除的状态契约

用户补全日志显示 55 个精确查询完成，失败来自临时对象删除 `NXError=1105004`，随后程序按保护策略停止。这不是已证实的 AskBoundingBoxExact 崩溃。旧代码无条件先删 CSYS、再删其矩阵，没有验证 tag 是否仍存活；旧日志不区分删除角色，尚不能证明本次具体是哪个 tag 失效。

- [UF_OBJ_delete_object](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_obj/global.html#UF_OBJ_delete_object) 明确：成功返回也不保证对象已删除，应使用 UF_OBJ_ask_status 确认。
- [UFObj.AskStatus(Tag)](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a80066.html) 的 .NET 签名在官方类页核对为返回 int；[原生契约](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_obj/global.html#UF_OBJ_ask_status) 返回四种状态，并提示避免操作失效对象；NX 可删除或复用它们。
- [uf_obj_types.h](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_obj/uf_obj_types.html) 定义 `UF_OBJ_DELETED=0`、`UF_OBJ_TEMPORARY=1`、`UF_OBJ_CONDEMNED=2`、`UF_OBJ_ALIVE=3`。实现使用对应 UFConstants 名称，非猜测错误码或反射接口。
- [CreateTempCsys](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_csys/global.html#UF_CSYS_create_temp_csys) 创建临时 CSYS 并接收关联矩阵。文档没有保证二者可以永远作为两个独立存活对象连续删除；也没有足够依据宣称在所有情况下删除 CSYS 必定删除矩阵。

修复在 NxGeometry 的公共测量服务内，按 CSYS→matrix 顺序先查状态再删除，删除后复核。已删除对象不再重复调用 DeleteObject；只对本次持有的 Alive/Temporary 对象尝试清理；CSYS 清理不确定时不继续删矩阵。1105004 只有在后续 AskStatus 明确返回 Deleted 时才接受，否则仍失败/停止/保留部件；不吞掉状态查询错误，不将 Condemned 当作删除完成。创建/查询失败与取消仍经 finally 清理。

新增创建/删除角色、tag、前后状态、实体进度与 DLL/MVID 标识，完整异常链写文件。265 项离线断言包含连带删除与独立删除、实际未删除、创建失败及后续事务/卸载路径；net472 引用编译和语法检查通过。原生状态与新 API 的 SDK 编译仍由本地确认。单独截图报错目前缺少对应日志，不因修复测量清理就视为已解决。

## 版本、构建和部署

- [2512 Product Notes](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/nx_rn/xid1753866/kuy1734360601359/fxn1734360624438/xid385122.html)：Designcenter Open 更名不改变 NXOpen API 名称；支持 .NET Framework 4.8 和 .NET 8。此分支默认 **net48 / x64**，不强制迁移 .NET Core。针对用户原工程目标新增显式 `v4.7.2` 试编译选项，不能据此认定 NX 2512 官方支持 4.7.2 运行环境。
- [Compiling and linking .NET](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/nx_api/nxopen_prog_guide/nel1734216491521/tms1734216491827/compiling_net.html)：Framework 引用来自 `NXBIN/managed`；Core 引用来自 `NXBIN/managed_core`。项目只引用 2512 Framework 版本，`Copy Local=false`，不分发 Siemens DLL。
- [Release upgrades](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/nx_rn/xid1753866/kuy1734360601359/fxn1734360624438/genid_release_upgrades_49_5026.html)：跨功能版本需要用目标版本 SDK 全量重编译和回归，不能把旧 DLL 能加载等同于已兼容。此处完成源代码审查，本地仍需实际编译。
- [Signing, language-specific details](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/nx_api/nxopen_prog_guide/xah1734216506204/xid1858041.html)：嵌入安装目录 `UGOPEN/NXSigningResource.res`，由本地 `NXBIN/SignDotNet` 签名并验证。项目已引用该资源，仓库不包含 Siemens 资源文件或二进制。

## 编译目标与宿主运行时

用户补充原 csproj 使用 4.7.2、历史编译成功，但尚未试编译本分支，也没有 4.8 缺失报错。`TargetFrameworkVersion` 是项目的编译目标，不代表机器最高可支持的框架。

- [Microsoft 组件版本兼容性](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/version-compatibility)：类库随宿主运行时运行；较低编译目标不会把 NX 的运行时降级。
- [MSB3644](https://learn.microsoft.com/en-us/visualstudio/msbuild/errors/msb3644)：缺少目标框架引用程序集时，应安装相应开发包或使用官方引用程序集包。不要直接根据旧 csproj 推断缺失。
- [Microsoft 官方 net48 引用包](https://www.nuget.org/packages/Microsoft.NETFramework.ReferenceAssemblies.net48/1.0.3)：可以提供编译引用，本身不是 NX SDK 或可部署的运行库。

构建脚本默认 v4.8，可显式指定 v4.7.2；中间文件、产物和日志按目标隔离。脚本把 MSB3274/MSB3275 作为失败处理，不通过旧 NX 引用或屏蔽警告来强行完成降级构建。需要本地 NX 2512 引用库的实际编译结果才能确定 v4.7.2 是否可用。

本次使用 Microsoft 官方 net472 引用程序集编译了无 NX 依赖的 `BoundingBoxMath.cs`、`ExportUtilities.cs` 和核心测试程序，并检查了两种目标的 MSBuild 项目求值。未在 Framework 4.7.2 运行时执行测试，也未以 NX SDK 编译完整插件。

## 此前：主循环与事务责任边界

对照原版 `2470a54` 的 BuildTransactionHandlers/Process/Finish 主线，本次保持顺序事务列表，新增显式注册重载而不把递归写进事务。标准接口为 `Process(TransactionContext)` + `Finish()`；这是源码接口调整，本地自定义扩展需迁移并重新编译，旧的直接 Process(Component, Session, ...) 入口已移除。README 给出责任表和接入示例。

- `AssemblyTraverser` 唯一拥有目标上下文生命周期、事务派发/统计/错误策略、逐节点资源释放及整轮 Finish。启动检查失败也收尾；每事务成功返回须包含公共画面恢复和上下文核验。
- `TransactionContext` 是借用输入与服务入口，没有公开构造或 Dispose。元数据事务不初始化显示范围；需要截图/精确测量时才创建内部 NxAssemblyExportContext，一目标共享一次。
- 原装配显示/视图状态由 NxAssemblyExportContext 管理；原先混在该类及 BoundingBoxExporter 的实体枚举、坐标变换、精确查询/临时对象清理移入 NxGeometry。两个事务只承担输出职责。公共截图服务在每事务后恢复画面，目标分支遮罩在全部事务结束后恢复。
- 官方 BasePart.LoadThisPartPartially 对已经部分加载的部件无作用。因此跳过已部分/完整加载部件的重复调用，同时保留 NxPartResources.BeforeLoad 的使用登记，确保不会破坏卸载所有权规则。
- RunLog 不再决定取消；TraversalCancellation 包装带日志的 NX 调用。取消入口、原生信号限制、清理绕过取消的规则不变。

本次没有新增 Siemens API，沿用下文已核对的 2512 接口。新的主要原生风险是每事务恢复的时机变化，需本地测试截图→测量的逆序、失败/取消、临时加载和显示状态。不能用离线契约测试代替真实 NX 状态验证。

## 已有：整轮停止与“工作进行中”的取消

[UFAbort .NET 2512](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a77630.html) 提供 `AskFlagStatus(out bool)`、`ClearAbort()`、`DisableAbort()`、`EnableAbort()`。本次只使用前两个。不存在已核实的 AskAbort 调用，不能根据名字猜接口。

[UF_ABORT_ask_flag_status 官方说明](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_abort/global.html#UF_ABORT_ask_flag_status) 明确：尚未处理的中断请求可读到 true；**NX 已处理请求后，标记已重置，查询返回 false**。因此不能承诺只在每个 NX 调用返回后轮询就能追溯所有“工作进行中”取消。ClearAbort 仅用于已经处理的请求；本次先锁存应用层停止，再清掉观测到的原生信号，避免后续节点恢复执行。

本次另加独立 Windows Forms 停止按钮，使用 Framework 自带引用，无 NX API 在 UI 线程执行。点击只设置 managed volatile 停止标记；主线程在当前调用返回、下一视角/实体/成员/事务/组件之前检查。NX 原生调用不被强制终止，恢复和释放代码绕过停止检查，普通异常与 OperationCanceledException 分别记录。没有猜取消错误码、匹配语言相关错误文字、全局 DisableAbort、线程强杀或自动点击原生对话框。

窗口代码使用独立 STA 消息循环；正常结束等待线程退出。若退出超时，插件暂保留至 NX 终止，并拒绝新任务，避免仍存活窗口访问已卸载代码。Windows 窗口源码已用官方 net472 引用独立编译；模型窗口/进度框存在时按钮是否响应、是否混入 PNG、窗口关闭和 NX 插件生命周期仍需本地验证。

## 逐项核对

| 调用/问题 | 官方 2512 依据 | 本分支处理 |
|---|---|---|
| `BlockDialog.Show` | [BlockDialog](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a38314.html)，自 NX2206 弃用 | 改 `Launch()`，只接受 `DialogResponse.Ok`；Apply 只验证，取消不执行 |
| Apply/Initialize 回调 | [Block Styler callbacks](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/nx_api/nxopen_prog_guide/ttj1734216502825/wba1734216502952/block_styler_callbacks.html) | 即使 DLX 只显示 OK/Cancel 也保留必需回调 |
| `UFSession.UI`、猜测的 `AskSuppressDialogs/SetSuppressDialogs` | [UFSession](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a80986.html)、[UFUi](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a80778.html)：会话属性为 `Ui`，没有原代码猜测的抑制接口 | 删除整段不存在/未证实的调用和反射常量，不使用“吞掉所有对话框”策略 |
| `SetActiveDisplay`、工作组件恢复 | [PartCollection](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a70134.html) | 本次已完全移除 NxPartContext；测量与截图均留在原 work/display，不再使用此激活接口 |
| 按需加载/只读诊断 | [BasePart](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a38266.html)、[ComponentAssembly](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a37866.html) | `OpenComponents(ComponentOnly)`、`LoadThisPartPartially`；检查加载结果；使用 `IsReadOnly/IsModified` 记录状态 |
| 包围盒坐标含义 | [UF_MODL_ask_bounding_box_exact](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_modl/global.html#UF_MODL_ask_bounding_box_exact)、[UFModl .NET](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a79234.html) | `Tag.Null` 表示当前 WCS；CSYS 必须位于当前 work part 上下文；八角点必须使用方向向量，不可直接 `min + distances` |
| ACS/WCS | [UFCsys](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a77982.html)、[UF_CSYS](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_csys/global.html)、[WCS](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a82066.html) | 读取目标 ACS/WCS 方向，经实例放置旋转转换到原装配；WCS 原点换算单位后变换。均在原工作装配创建临时 CSYS，不把原型 WCS tag 直接传给装配查询 |
| 临时对象清理 | [UF_OBJ_delete_object](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_obj/global.html#UF_OBJ_delete_object) | 只删除本次创建的临时 CSYS/矩阵，不修改、替换或删除源 WCS |
| 装配体测量 | [Component.FindOccurrence](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a37862.html) 及上述 UF_MODL 文档 | 所有非根目标使用 occurrence tag；根装配另计本身实体。不逐个 FindOccurrence 原型实体，避免强行创建参考集外实体的持久 occurrence；详见下方实例成员筛选 |
| 视图和图片 | [View](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a81942.html)、[ImageExportBuilder](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a61974.html) | 保留有文档的 `Orient`、`SetRotationTranslationScale`、PNG/Transparent/EnhanceEdges；Builder 在 finally 销毁 |

## Teamcenter 只读弹窗：确定的代码行为与待实测原因

**源码已证实**：旧版逐图切换 `part.WCS.Visibility`，把三轴显示强制改成固定值，执行 `CleanUpFacetedFacesAndEdges`，遍历后主动 `Close(...UseResponses, null)`，并且只读抑制代码本身包含无效 API。它还在 Block Styler 对话框对象尚未释放时执行导出。

**诊断推断**：这些写入/清理/关闭操作遇到只读或已修改的受管部件时，是弹窗的可疑触发点；未连接实际站点，不能断言它们就是每个弹窗的唯一原因。前一版删除这些操作，并在对话框释放后执行。本次根据大装配反馈重新引入有所有权边界的逐个卸载，详见下节。不会签出、保存、改变 Teamcenter ACL、修改发布状态或清除源部件修改标记。

**当前保留的操作**：按需加载、装配实例显示/隐藏、视图旋转和坐标轴显示开关；测量使用原装配临时坐标系，已删除原型显示/工作切换。隔离截图已按用户反馈撤除。加载旧版本零件、站点自定义回调和显示切换本身仍可能产生提示或修改标记。官方还说明 `SetActiveDisplay` 可能重置撤销标记列表；恢复 display/work/view 不等于恢复 undo 历史。本地应先在独立测试会话验收。

若仍弹窗，用 `Traversal.log` 最后一条 `[Load geometry]` / `[Context]` / `[BoundingBox]` / `[Orient]` / `[Restore view]` 定位阶段，记录 NX 错误号、弹窗完整文字、2512 maintenance build、该对象是否已发布/被他人签出、当前 work/display 状态及 NX syslog。不要直接加猜测的 suppress API 或强行签出来掩盖问题。

## 本次实测日志后的加载/卸载核对

用户日志提供了 `641058` 的实际中文含义“已修改只读部件”，以及 `641070`“不是该组件部件的合法版本”。这两个数值的映射来自用户运行证据；官方资料进一步说明只读加载修改的机制，不把数值出处冒充为官方枚举表。

| 接口/行为 | 官方 2512 依据 | 本次处理 |
|---|---|---|
| 加载只读文件也可能改变内存 | [UF_PART_open](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_part/global.html#UF_PART_open) 明确：读取过程中修改了只读部件，会返回非零 `UF_PART_err_read_only_modified`；这不是执行 Save 的证明 | `641058` 单独记录为警告，随后检查实际加载状态；其他代码仍失败，不忽略混合状态中的其他错误 |
| 部分与完整加载 | [BasePart](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a38266.html)：`LoadThisPartPartially()` 把最小加载提升到至少部分加载，不加载子件；`PartLoadState` 返回实际状态，`IsFullyLoaded` 验证完整加载 | 两种导出共用当前实例未隐藏/未抑制后代的部分加载；删除 CollectBodyTags/LoadFully。精确查询可能隐式读取几何，不能据此保证其内部不会升级加载；无可用精确实体时明确失败 |
| 等显示操作完成 | [DisplayManager.MakeUpToDate](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a53102.html) | 加载显示数据后完成待处理显示操作再截图；图像完整性仍需实机验收 |
| 枚举与保护会话 | [PartCollection](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a70134.html)：`ToArray()` 返回 BasePart[]；`GetDisplayedParts()` 返回全部显示部件；`BaseWork` 返回工作部件 | 建立初始状态基线；保护所有显示窗口、work 及 [Component.Parent/OwningPart](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a37862.html) 工作路径；活动递归祖先引用计数保护 |
| 逐个关闭临时部件 | [BasePart.Close/CloseModified](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a38266.html)：wholeTree=false 只卸载指定部件；CloseModified 允许关闭已修改部件；responses 仅在 UseResponses 时使用 | 明确 False + CloseModified + null，仅作用于临时部件及本次触碰/提升的初始干净部分加载部件；不保存，舍弃其本次内存变化。禁止整树/CloseAll；关闭后验证不再出现在 Parts 集合 |
| 关闭与内存不完全等价 | [UF_PART_close](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_part/global.html#UF_PART_close) 特别说明该 UF 函数不删除 undo marks，因此关闭后仍可能保留大量内存 | 采用上面的 BasePart API，不假定其一定立即降低进程内存；不执行 UF_UNDO_delete_all_marks。记录实际已加载数和 PrivateMB，由本地测量确认 |

关闭已修改临时部件时不走“询问是否保存”的选择路径，但公开接口依据不足以保证屏蔽 NX 返回交互界面后的所有只读消息、Teamcenter 站点扩展的对话框。没有使用 SAP 集成专用环境变量、猜测 suppress API、反射、键盘自动确认、改变权限或清除修改标记。

清理覆盖加载异常（连带打开的依赖）、导出异常、取消和正常返回。关闭失败停止后续加载；保留部件新增修改标记输出 `[WARN retained modified]`。同一原型跨卸载重载以路径去重，避免 tag 改变造成重复导出。

新增独立测试使用 `PartResourceTests.cs` 中明确标记的 NX 测试替身，运行真实资源管理器控制流；验证保护/释放/拒绝关闭分支，不验证 NX SDK 签名、几何生命周期或原生弹窗。后者仍交本地 2512/Teamcenter 验收。

## 当前路线：原装配内显示目标，图片按目标自身 ACS

用户提出以原装配的显示/隐藏取代单独激活或隔离窗口，并明确图片按零件/子装配自身 ACS。不能用 Teamcenter 的结构配置推断 WCS 与 ACS 必然重合。当前只改变原装配视图相机，不修改 WCS、不移动组件。

| 当前接口 | 官方 2512 依据及实现边界 |
|---|---|
| [DisplayManager.BlankObjects/UnblankObjects](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a53102.html) | 隐藏/显示给定 DisplayableObject 数组；面/边不属于有效输入，会被忽略。对目标路径外的兄弟实例、目标外可见对象和截图范围内的辅助对象操作，不调用原型激活、隔离或布局 API；批调用前记录原状态，应对部分应用后抛错 |
| [DisplayableObject.IsBlanked](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a53098.html)、[Component.Parent/GetChildren](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a37862.html) | 沿当前显示装配中的实例路径隐藏其他分支，必要时显示目标/祖先，保留隐藏后代；读取并复核 blank 状态，恢复时不使用 ShowAll。没有文档承诺此操作不产生修改标记或站点提示 |
| [View.AskVisibleObjects/Fit/FitToObjects](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a81942.html) | AskVisibleObjects 不包括隐藏/不可见层和 X/Y 裁剪范围外的对象，不能单独枚举它后就认为“隐藏了所有其他对象”。因此每个方向先 Fit、刷新，再隐藏可见的非目标对象；最后 FitToObjects(target)、刷新并复核。空目标/仍有其他对象时失败。Component 实现 IFitTo |
| [NXObject.OwningComponent](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a69838.html) | 返回 occurrence 的所属组件，沿 Parent 判断目标后代。按实例 tag 判断，不按原型相等判断，避免把同一原型其他实例也纳入图片。非 occurrence 对象仅允许当前显示装配所有的对象，否则拒绝修改 |
| [Face.GetBody](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a54578.html)、[Edge.GetBody](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a54418.html) | 将可见面/边归并为所属 Body 后再隐藏，避免 BlankObjects 忽略；这两个查询需要 SOLIDS MODELING 许可，有许可问题时报告失败，不退回修改原型 |
| [UFAssem.AskTransformOfOcc/AskViewExplosion](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a77634.html)、[UF_ASSEM_ask_transform_of_occ](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_assem/global.html#UF_ASSEM_ask_transform_of_occ) | 查询实例 4×4 变换；在当前显示装配的 occurrence 上使用旋转 R。View.Matrix 定义为模型到视图的旋转，因而相机采用 C×R逆，平移交给 FitToObjects。矩阵必须正交，拒绝镜像/剪切/非均匀缩放和爆炸视图。嵌套实例、安装位置与 NX 原生矩阵约定仍须实机图片比对 |
| [View.WcsVisibility/TriadVisibility](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a81942.html)、[WCS.CoordinateSystem](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a82066.html) | 截图使用视图级开关临时隐藏坐标轴，并恢复原值；不读写 WCS 来决定图片方向。WCS 是独立的工作坐标系对象，不能因结构从 Teamcenter 打开就假定与 ACS 一致 |
| [ImageExportBuilder](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a61974.html) | RegionMode=false、Png、Transparent、EnhanceEdges、Commit/finally Destroy；普通图形区域尺寸，无固定 1600×1200。本实现要求原装配 work=display 且只有一个活动建模视口，不创建新窗口 |

`NxAssemblyExportContext` 由用户确认截图路线的 NxAssemblyImageContext 扩展并更名，现为内部显示服务；两种导出经 TransactionContext 共用一次原装配显示范围，并在每事务后恢复画面。结束时尝试恢复所有状态类别；任何恢复错误停止并保留当前加载集合。仅在全部恢复并核验原 work/display/work component/活动视口后才由资源管理器卸载。显示操作可能隐式加载；每批记录 LoadedParts，仍需本地确认内存峰值和恢复显示的成本。

图片内容采用首次匹配实例的参考集及隐藏状态，方向按原型 ACS；并非保证与“单独打开完整原型”显示完全相同。不改参考集、图层、源 WCS、不 Save/Checkout/清 IsModified。精确测量按目标自身 ACS/WCS 的方向，在原装配上下文查询当前显示范围的实体。旧 `useIsolatedImageView=true` 仍被拒绝，无图像失败后的原型激活回退。

**验证边界**：用户先前日志只确认一次提示发生于 SetActiveDisplay 内，未定位其内部原因。当前截图和 XYZ 都避开该调用，但 Blank/Unblank、部分加载、视图变化仍有原生副作用，不能由文档存在或离线替身测试推出无弹窗结论。原生显示状态是否完全恢复、层/参考集效果、窗口数、图片标记和内存峰值均待本地验收。

## 本次新增：辅助对象隐藏与原装配精确测量

用户已确认 `5478c9e` 的截图路线符合预期，要求补齐辅助对象隐藏，并让 XYZ 同样留在原装配。范围确认：隐藏零件不计入尺寸。以下为本次实现依据，不能代替本地 SDK 编译与实机验收。

| 接口/行为 | 官方 2512 依据与实现 |
|---|---|
| 截图辅助对象 | 上述 DisplayManager / DisplayableObject / View：可见面和边归并为 Body；保留 Body 和 [Facet.FacetedBody](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a54710.html)，其余坐标系、点、草图曲线、基准/注释等临时 Blank。逐对象记录原始状态，异常也恢复；不采用难以恢复原状态的 HideByType/ShowAll。目标无模型对象时失败，不能把辅助几何误当成成功截图 |
| 实例成员与子组件 | [UFAssem.CycleObjsInComp](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a77634.html)、[原生契约](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_assem/global.html#UF_ASSEM_cycle_objs_in_comp)、[官方遍历示例](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_assem/uf_assem_eg3.c)：枚举组件的 occurrence 成员，以 Tag.Null 开始/结束；示例另行遍历子组件，不能假定成员循环自动覆盖所有后代。本实现亦单独递归 GetChildren，并过滤隐藏/抑制状态。需实测参考集排除子件、部分加载和轻量化状态下成员覆盖情况 |
| UF tag 转 NX 包装对象 | [NXObjectManager.Get](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a69870.html)：静态 Get(Tag) 返回 TaggedObject；只将当前存活 tag 转为 DisplayableObject，不持有已卸载对象用于后续目标 |
| 层与范围 | [LayerManager.GetVisibilitiesInView](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a64154.html)：要求当前显示部件；返回 StateInfo[]，第 i 项对应层 i+1，仅 Visible/Hidden。[StateInfo](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a64142.html) 的 Layer/State 是字段。筛掉隐藏层、blank 对象及隐藏/抑制祖先，不修改层状态。测量不依赖相机可见集合，避免漏掉未隐藏但被遮挡的实体 |
| 轻量化对象 | FacetedBody.IsAssociatedBodyLoaded / AssociatedBody 返回关联实体的加载状态/Body。只有存在原装配内对应实体时才转为精确查询；仅有小平面数据时测量失败，不静默漏算，不改为粗略盒或完整激活原型。截图仍可保留小平面模型 |
| 原装配精确盒 | UF_MODL_ask_bounding_box_exact 明确支持 occurrence tag，会按实例变换到装配空间；CSYS 必须属于当前 work part。将目标 ACS/WCS 组合实例旋转后，在原装配 CreateMatrix/CreateTempCsys，直接按该方向查询几何，不旋转已有 ACS 包围盒。原型 WCS 只读；临时对象 finally 删除，删除失败停止 |
| 单位与坐标 | UF_ASSEM_ask_component_data 说明 4×4 的旋转块和位移列，位移以装配单位表示。目标 WCS 原点先换为装配单位，再变换；结果从装配单位换为目标单位。混合毫米/英寸及任意角度/嵌套实例须本地手工精确盒对照，数学替身不能验证 NX 数据单位的原生返回行为 |

`NxAssemblyExportContext` 不修改参考集、组件位置、源 WCS 或访问权限。截图与 XYZ 在同一范围中顺序执行；一次测量失败可继续截图，恢复或资源清理失败则停止。删除 NxPartContext 和显式完整加载函数，旧原型激活路径不再有调用入口。

CSV 首五列兼容，范围列为 `DisplayedOccurrenceBodies`；同一原型不同实例仍只导出第一个匹配实例。不能把这次范围变化误写成“完整原型所有后代”。任一精确查询失败不写该目标尺寸行；日志包含实体数和每次精确查询 begin/end，供进一步定位站点提示。

未采用的屏蔽候选：

- [UI.UserInterfacePreferences](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a81018.html) / [DisplayAlertsOnWarnings](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a72018.html) 的文档范围为特征创建/编辑/更新诊断，不是已证实的只读通知开关。
- [MessageBoxTester](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a38486.html) 要求 Automated Testing Studio 许可；自动选择问题框答案不适合作为通用插件策略。
- 不使用已退役的 SetWorkPartQuietly，不用 UF SetDisplayPart 替换后就假定没有相同显示副作用。

## 已废弃的隔离实验及错误判断记录

这些记录用于解释失败，不是当前代码或本地验收要求。

- [CreateIsolateViewWithComponents](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a37866.html) 允许创建临时隔离视图，直至不再显示。它的创建契约并不保证我们组合的退出调用能关闭窗口，也不保证截图无边框/编号。之前只核对了单个接口，就把整个生命周期作为默认方案，证据不足。
- `a8a8e63`、`0a6a00b` 的 Layout.Open 返回 925052「要打开的布局已显示」。第二版只比较 Current tag，仍失败。Current 不同不能证明布局可以 Open；测试替身也错误简化了这两个状态。
- `e5a7cc2` 改用 [LayoutCollection.ChangeLayout](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a64322.html)，实机返回 580039「使用了一个已删除或无效的类号」。用户确认画面已回到原装配，证明视觉切换发生，但不能确定清理完整或错误的具体内部来源。
- `053a2fc` 曾加入异常后重新枚举状态、满足严格条件才允许卸载的分支；这仅为判定逻辑补丁，没有真实 NX 验证。没有找到独立、明确的 EndIsolate 退出接口。随后用户补充窗口累积和图像标记问题，`bfd0c82` 完整删除隔离实现与这些退出补丁，不要求继续录制退出 Journal。
- 之前的隔离测试替身只能证明代码符合我们写下的假设，不能证明原生 NX 的窗口、对象生命周期或渲染行为。相关测试随废弃实现移除；新的装配内截图测试也遵守同一验证边界。

## 关闭后的 3615013：失效对象与会话复核

用户日志在 Close 已返回成功后立即报“试图使用不活动的对象”，随后父节点清理仍将 LoadedParts 降回 1。代码审查发现：旧循环跨 Close 保留整个 BasePart[]，下一个对象可能已被关闭连带失效。这是与日志吻合的实现缺陷，不能把这一次异常直接认定为未释放。

改为跨调用只保留 tag/名称，下一候选从当前 Parts.ToArray 重新取得。每次关闭后再枚举：目标消失则记为释放；只在目标确已消失时容忍关闭抛出的 3615013，仍在会话或其他错误均停止。同时检查受保护/活动祖先是否意外消失。没有把 3615013 全局吞掉，也没有扩大关闭范围。

测试替身专门模拟父关闭使子包装对象无效、关闭后抛 3615013、关闭前抛且目标仍存在；真实 NX 关闭行为仍需本地复核。

## 其他审查修复及边界

- DLX XML 声明前空行使 XML 无法严格解析；`.gitignore` 也错误忽略 DLX。已修复。`NX="1946"` 保留真实生成器来源，需本地 Styler 验证/另存，不能只改数字冒充格式升级。
- 中文 WCS 选项原先落入 ACS 默认分支；现在显式映射，不接受未知值。
- 原 WCS 算法旋转的是 ACS 外包围盒，不是实体，会高估；原 ACS 又错误依赖当前 WCS。已直接按目标坐标系查询并正确聚合方向向量。
- 不再用坐标绝对值大于 1e6 的规则静默过滤实体；拒绝 NaN/Infinity/负尺寸；任一实体失败则不写不完整 CSV。
- 多个实例共享同一个原型只导出一次；这里输出的是**首次匹配实例当前显示范围**的尺寸，ACS/WCS 方向属于该目标原型，不是顶层装配的每实例坐标报表。匹配子装配后不再单独导出其子件，沿用原筛选语义。
- 已按本次用户要求改为逐节点卸载临时部件，保护用户原有完整加载/已修改/活动部件及递归祖先；详见下节。默认遍历深度仍为 5，可通过 `maxDepth` 调整。单个匹配大装配及 NX 缓存仍可能造成较高峰值，需实测。
- 当前取消由独立停止按钮、外部 `AssemblyTraverser.RequestStop()` 和可读到的 NX 取消信号触发；锁存本轮停止，当前调用返回后退出后续工作，恢复/释放仍执行。不能把 ESC 或原生进度框取消视为每次都可可靠追溯的整轮停止入口。
- 每次输出到新的子目录，CSV 首五列兼容原字段，新增单位/坐标系/范围/实体数；名称转义、小数点固定、中文 UTF-8 BOM、PNG 文件名防冲突。
- 恢复截图隐藏 WCS/三重轴的原有特性，改用 View 局部开关并在 finally 恢复原值；不恢复原版强制开关为固定值的副作用，不执行源几何清理。

## 验证边界

本次已执行 DLX/XML/映射检查及 265 项独立 C# 断言。数学/文本/所有权策略之外，真实 AssemblyTraverser/TransactionContext/RunLog/取消监控、资源管理器、原装配共享上下文、NxGeometry 部分加载选择、装配内截图及实际 BoundingBoxExporter 代码与显式 NX 测试替身组合编译并验证控制流；替身不证明官方 API 签名或原生行为。还以官方 net472 框架引用完成同一测试程序的编译，未在 Framework 4.7.2 运行。未执行 NX SDK 全量编译、NX 原生 DLX 加载、PNG 渲染、Teamcenter 实机测试或本地签名；这些由 [本地 agent 交接](../LOCAL_AGENT_PROMPT.md) 列出。纯算法测试通过不等于 NX 2512 集成验收通过。
