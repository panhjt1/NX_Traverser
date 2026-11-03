# NX 2512 官方 API 核对与审查记录

核对日期：2026-11-03。基线：`2470a54`。NX API 核对以 **Siemens 官方 2512 文档**为依据，不把旧版社区示例视为新版本 API；用户运行证据和 Microsoft 构建资料另行注明。官方文档浏览器页面动态加载，本文链接到同一文档的正文或参考类。

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

## 逐项核对

| 调用/问题 | 官方 2512 依据 | 本分支处理 |
|---|---|---|
| `BlockDialog.Show` | [BlockDialog](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a38314.html)，自 NX2206 弃用 | 改 `Launch()`，只接受 `DialogResponse.Ok`；Apply 只验证，取消不执行 |
| Apply/Initialize 回调 | [Block Styler callbacks](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/nx_api/nxopen_prog_guide/ttj1734216502825/wba1734216502952/block_styler_callbacks.html) | 即使 DLX 只显示 OK/Cancel 也保留必需回调 |
| `UFSession.UI`、猜测的 `AskSuppressDialogs/SetSuppressDialogs` | [UFSession](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a80986.html)、[UFUi](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a80778.html)：会话属性为 `Ui`，没有原代码猜测的抑制接口 | 删除整段不存在/未证实的调用和反射常量，不使用“吞掉所有对话框”策略 |
| `SetActiveDisplay`、工作组件恢复 | [PartCollection](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a70134.html) | 测量/显式旧截图用 `ReplaceExisting` + `SameAsDisplay` 建立原型上下文；默认隔离截图不调用；恢复原 display/work occurrence，使用 `PreserveRefset`；释放加载状态 |
| 按需加载/只读诊断 | [BasePart](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a38266.html)、[ComponentAssembly](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a37866.html) | `OpenComponents(ComponentOnly)`、`LoadThisPartPartially/Fully`；检查加载结果；使用 `IsReadOnly/IsModified` 记录状态 |
| 包围盒坐标含义 | [UF_MODL_ask_bounding_box_exact](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_modl/global.html#UF_MODL_ask_bounding_box_exact)、[UFModl .NET](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a79234.html) | `Tag.Null` 表示当前 WCS；CSYS 必须位于当前 work part 上下文；八角点必须使用方向向量，不可直接 `min + distances` |
| ACS/WCS | [UFCsys](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a77982.html)、[UF_CSYS](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_csys/global.html)、[WCS](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a82066.html) | ACS 用临时单位矩阵 CSYS；WCS 使用目标原型的 `WCS.CoordinateSystem.Tag`，直接请求按该坐标系对齐的精确包围盒 |
| 临时对象清理 | [UF_OBJ_delete_object](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_obj/global.html#UF_OBJ_delete_object) | 只删除本次创建的临时 CSYS/矩阵，不修改、替换或删除源 WCS |
| 装配体测量 | [Component.FindOccurrence](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a37862.html) 及上述 UF_MODL 文档 | 后代使用 occurrence tag 取得装配空间的包围盒；叶子使用原型 tag；参考集不含实体导致无法取得 occurrence 时明确失败 |
| 视图和图片 | [View](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a81942.html)、[ImageExportBuilder](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a61974.html) | 保留有文档的 `Orient`、`SetRotationTranslationScale`、PNG/Transparent/EnhanceEdges；Builder 在 finally 销毁 |

## Teamcenter 只读弹窗：确定的代码行为与待实测原因

**源码已证实**：旧版逐图切换 `part.WCS.Visibility`，把三轴显示强制改成固定值，执行 `CleanUpFacetedFacesAndEdges`，遍历后主动 `Close(...UseResponses, null)`，并且只读抑制代码本身包含无效 API。它还在 Block Styler 对话框对象尚未释放时执行导出。

**诊断推断**：这些写入/清理/关闭操作遇到只读或已修改的受管部件时，是弹窗的可疑触发点；未连接实际站点，不能断言它们就是每个弹窗的唯一原因。前一版删除这些操作，并在对话框释放后执行。本次根据大装配反馈重新引入有所有权边界的逐个卸载，详见下节。不会签出、保存、改变 Teamcenter ACL、修改发布状态或清除源部件修改标记。

**保留的必要操作**：按需加载、临时隔离视图、视图旋转和坐标轴显示开关；测量/显式旧截图仍切换显示/工作上下文，测量仍使用临时坐标系。加载旧版本零件、站点自定义回调和显示切换本身仍可能产生提示或修改标记。官方还说明 `SetActiveDisplay` 可能重置撤销标记列表；恢复 display/work/view 不等于恢复 undo 历史。本地应先在独立测试会话验收。

若仍弹窗，用 `Traversal.log` 最后一条 `[Load geometry]` / `[Context]` / `[BoundingBox]` / `[Orient]` / `[Restore view]` 定位阶段，记录 NX 错误号、弹窗完整文字、2512 maintenance build、该对象是否已发布/被他人签出、当前 work/display 状态及 NX syslog。不要直接加猜测的 suppress API 或强行签出来掩盖问题。

## 本次实测日志后的加载/卸载核对

用户日志提供了 `641058` 的实际中文含义“已修改只读部件”，以及 `641070`“不是该组件部件的合法版本”。这两个数值的映射来自用户运行证据；官方资料进一步说明只读加载修改的机制，不把数值出处冒充为官方枚举表。

| 接口/行为 | 官方 2512 依据 | 本次处理 |
|---|---|---|
| 加载只读文件也可能改变内存 | [UF_PART_open](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_part/global.html#UF_PART_open) 明确：读取过程中修改了只读部件，会返回非零 `UF_PART_err_read_only_modified`；这不是执行 Save 的证明 | `641058` 单独记录为警告，随后检查实际加载状态；其他代码仍失败，不忽略混合状态中的其他错误 |
| 部分与完整加载 | [BasePart](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a38266.html)：`LoadThisPartPartially()` 把最小加载提升到至少部分加载，不加载子件；`PartLoadState` 返回实际状态，`IsFullyLoaded` 验证完整加载 | 截图递归准备后代部分加载显示数据，去掉 `CollectBodyTags`；精确测量仍调用完整加载并验证。没有承诺 NX 内部不会自行升级加载 |
| 等显示操作完成 | [DisplayManager.MakeUpToDate](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a53102.html) | 加载显示数据后完成待处理显示操作再截图；图像完整性仍需实机验收 |
| 枚举与保护会话 | [PartCollection](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a70134.html)：`ToArray()` 返回 BasePart[]；`GetDisplayedParts()` 返回全部显示部件；`BaseWork` 返回工作部件 | 建立初始状态基线；保护所有显示窗口、work 及 [Component.Parent/OwningPart](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a37862.html) 工作路径；活动递归祖先引用计数保护 |
| 逐个关闭临时部件 | [BasePart.Close/CloseModified](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a38266.html)：wholeTree=false 只卸载指定部件；CloseModified 允许关闭已修改部件；responses 仅在 UseResponses 时使用 | 明确 False + CloseModified + null，仅作用于临时部件及本次触碰/提升的初始干净部分加载部件；不保存，舍弃其本次内存变化。禁止整树/CloseAll；关闭后验证不再出现在 Parts 集合 |
| 关闭与内存不完全等价 | [UF_PART_close](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_part/global.html#UF_PART_close) 特别说明该 UF 函数不删除 undo marks，因此关闭后仍可能保留大量内存 | 采用上面的 BasePart API，不假定其一定立即降低进程内存；不执行 UF_UNDO_delete_all_marks。记录实际已加载数和 PrivateMB，由本地测量确认 |

关闭已修改临时部件时不走“询问是否保存”的选择路径，但公开接口依据不足以保证屏蔽 NX 返回交互界面后的所有只读消息、Teamcenter 站点扩展的对话框。没有使用 SAP 集成专用环境变量、猜测 suppress API、反射、键盘自动确认、改变权限或清除修改标记。

清理覆盖加载异常（连带打开的依赖）、导出异常、取消和正常返回。关闭失败停止后续加载；保留部件新增修改标记输出 `[WARN retained modified]`。同一原型跨卸载重载以路径去重，避免 tag 改变造成重复导出。

新增独立测试使用 `PartResourceTests.cs` 中明确标记的 NX 测试替身，运行真实资源管理器控制流；验证保护/释放/拒绝关闭分支，不验证 NX SDK 签名、几何生命周期或原生弹窗。后者仍交本地 2512/Teamcenter 验收。

## 最新实测：预加载未避开激活提示，截图改为临时隔离

用户明确指出弹窗出现时最后一条为 `SetActiveDisplay (activate target)` 的 begin；前一条完整加载已经成功，目标为 FullyLoaded、Modified=True。因此确认这一次提示发生在激活调用内，不能继续把显式预加载当作解决方案。此证据只定位到该调用，尚不能区分其中 NX 内部逻辑和站点回调。测量路径保留原型上下文；默认截图路径分离，不再显式完整预加载/激活目标原型。

| 接口 | 官方 2512 依据及实现边界 |
|---|---|
| [ComponentAssembly.CreateIsolateViewWithComponents](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a37866.html) | 创建临时 View，显示给定组件；装配仅一个隔离视图，直至它不再显示。返回 ErrorList 和 View；检查所有 ErrorCode，失败不自动回退到原型激活 |
| [View](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a81942.html) 的 MakeWork、Matrix、Orient、WcsVisibility、TriadVisibility | MakeWork 选择工作视图，本身不保证视图正显示，因此另查活动视图。Matrix 为模型空间到视图空间旋转。用视图级 bool 开关隐藏 WCS/三重轴并恢复原值，不写全局偏好或 part.WCS.Visibility；未变化的原视图不重复写回矩阵 |
| [UFAssem.AskTransformOfOcc / AskViewExplosion](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a77634.html) | 取组件 4×4 放置矩阵及当前视图的爆炸状态。对正常旋转应用 C × R逆，使原型方向不受实例放置旋转影响；忽略平移以 Fit 居中。拒绝爆炸、镜像、剪切和非均匀缩放。嵌套实例坐标仍需实机图片比对 |
| [ViewCollection](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a81946.html) | GetActiveViews、WorkView、IsWorkViewExpanded()、UnexpandWork()；仅接受普通单建模视口，捕获并核对展开状态和工作视图。必须工作部件=显示部件；调用方先退出已有隔离/制图/分屏状态 |
| [LayoutCollection.Current/ChangeLayout](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a64322.html)、[Layout.ReplaceView](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a64166.html) | ChangeLayout 将给定布局设为当前布局（自 NX6），恢复路径不再调用 Open；若临时建模视图仍显示，则 ReplaceView(old,new,false)。刷新显示后核对原布局、唯一活动视图、工作视图及展开状态才卸载 |
| [StudioImageCaptureBuilder](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a52978.html) | SourceType.WorkView（自 NX2206）只捕获工作视图；NativeFileBrowser 指定 .png，Custom/Pixels，SetImageDimensionsInteger 的顺序是高度、宽度，因此 [1200,1600]；透明背景、EnhanceEdges、Commit/finally Destroy |

**文档保证与实现推断分开**：官方保证隔离视图在不再显示时结束其隔离状态；没有找到专门的 EndIsolate 接口。本次使用上述布局/视图公开 API 组成恢复流程并检查结果，未在 NX 本机证实该组合在本站点能完整退出临时视图。单件及连续两轮实机验收必须先于大树运行。恢复失败时停止新加载并保留当前加载集合，避免关闭仍被视图引用的组件；不承诺已验证彻底无弹窗。

图片采用首次匹配实例的当前参考集、布置和隐藏状态，可能与旧原型激活截图和原型完整几何测量范围不同。`useIsolatedImageView=false` 可显式对比旧截图，仍有只读提示风险。两条路径都隐藏 WCS/三重轴并恢复原开关；八个方向、自动适配、透明背景和边缘增强沿用原功能。禁止为减少提示直接删除这些输出特性。

未采用的屏蔽候选：

- [UI.UserInterfacePreferences](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a81018.html) / [DisplayAlertsOnWarnings](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a72018.html) 的文档范围为特征创建/编辑/更新诊断，不是已证实的只读通知开关。
- [MessageBoxTester](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a38486.html) 要求 Automated Testing Studio 许可；自动选择问题框答案不适合作为通用插件策略。
- 不使用已退役的 SetWorkPartQuietly，不用 UF SetDisplayPart 替换后就假定没有相同显示副作用。

## 925052：当前布局与 Open 的已显示限制不能等同

首次实测中隔离创建、图像捕获均已返回成功，PNG 文件存在，但 Restore image layout 内的 Open 返回 925052「要打开的布局已显示」。后续 `0a6a00b` 加了 Current tag 检查，用户仍复现：日志进入“切回原布局”后 Open 抛出同一个错误。因此 Current 不同或为空不能证明原布局可以再次 Open；上版控制流和测试替身均未覆盖这个状态。错误号含义来自用户运行日志，不是官方枚举推断。

本次重新核对 Siemens 2512 LayoutCollection.ChangeLayout：文档明确定义其作用是把给定布局设为当前布局。恢复路径改为调用该接口，完全移除 Layout.Open，不以 DisplayStatus 或捕获 925052 作为恢复成功依据。Layout.DisplayStatus 的文档语义是“活动显示部件的当前布局”，不能拿它替代对 Open 行为的判断；后续诊断不再读取缓存布局对象。

切换后先验证 Current，再按需替换仍显示的隔离视图，恢复原工作视图、展开状态和视图参数。MakeUpToDate 后再次核对布局/活动视图/工作视图/展开状态。日志记录各阶段 tag。后续对 580039 的有条件恢复见下节；其他错误或未验证的状态仍停止新加载，保留当前加载集合。

回归替身将 Current 与已显示时拒绝 Open 的行为分开，并模拟隔离改变 Current，证实上一版失败、新实现通过；覆盖同布局替换、不同布局切换、部分创建失败及切换抛错/未生效。测试替身不证明 ChangeLayout 在实际站点中的退出效果，不能宣称本机验收完成。

## 580039：只能用调用后状态验证是否已恢复

最新用户实测提供了两个同时活动的视图：原视图仍在，新增隔离视图及当前布局。ChangeLayout 抛 580039「使用了一个已删除或无效的类号」，但原版本没有记录异常后的状态。用户随后确认报错后画面已返回原装配，说明界面切换已经发生；仍不能仅凭视觉反馈证明原生对象状态完整，也不能断言异常一定来自内部清理。

本次继续使用已核对的 ChangeLayout，不再猜测新的退出函数。按原 tag 从 [LayoutCollection.ToArray](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a64322.html) 和 [ViewCollection.ToArray](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a81946.html) 重取对象。GetActiveViews 与 GetDisclosedModelViews 分别确认原活动视图集合及隔离视图不再 disclosed；诊断各字段独立查询，避免一个无效包装对象掩盖全部状态。

仅在 ChangeLayout 抛 580039 且新查询确认原布局、唯一原活动视图、无残留隔离 disclosed view 时，继续恢复原工作视图与快照；显示刷新后再次核对全部上下文，才能记录 Image restore verified 并卸载。其他错误、状态不符或读取失败仍停止；错误日志保留，不把 Completed=False 改成 True。不清除用户修改标记或撤销历史。

额外核查了 [ViewUIManager.CreatePreview](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a81966.html)，它只创建预览，不是退出接口；[UF_VIEW_delete](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_view/global.html#UF_VIEW_delete) 明确限制删除工作视图及仍属于布局的视图，因此未拿它强删当前隔离视图。未找到具有明确退出契约的独立 EndIsolate API。

136 项离线断言包含完全恢复后报 580039、报错前未改变状态、双视图残留、disclosed 残留，以及其他错误不能放行。替身只验证代码的判定分支，不能证实本站点触发的是哪一类。若本机仍未退出，需按本地提示词采集手动退出的 Journal 和调用后状态；本次不宣称实机修复完成。

## 关闭后的 3615013：失效对象与会话复核

用户日志在 Close 已返回成功后立即报“试图使用不活动的对象”，随后父节点清理仍将 LoadedParts 降回 1。代码审查发现：旧循环跨 Close 保留整个 BasePart[]，下一个对象可能已被关闭连带失效。这是与日志吻合的实现缺陷，不能把这一次异常直接认定为未释放。

改为跨调用只保留 tag/名称，下一候选从当前 Parts.ToArray 重新取得。每次关闭后再枚举：目标消失则记为释放；只在目标确已消失时容忍关闭抛出的 3615013，仍在会话或其他错误均停止。同时检查受保护/活动祖先是否意外消失。没有把 3615013 全局吞掉，也没有扩大关闭范围。

测试替身专门模拟父关闭使子包装对象无效、关闭后抛 3615013、关闭前抛且目标仍存在；真实 NX 关闭行为仍需本地复核。

## 其他审查修复及边界

- DLX XML 声明前空行使 XML 无法严格解析；`.gitignore` 也错误忽略 DLX。已修复。`NX="1946"` 保留真实生成器来源，需本地 Styler 验证/另存，不能只改数字冒充格式升级。
- 中文 WCS 选项原先落入 ACS 默认分支；现在显式映射，不接受未知值。
- 原 WCS 算法旋转的是 ACS 外包围盒，不是实体，会高估；原 ACS 又错误依赖当前 WCS。已直接按目标坐标系查询并正确聚合方向向量。
- 不再用坐标绝对值大于 1e6 的规则静默过滤实体；拒绝 NaN/Infinity/负尺寸；任一实体失败则不写不完整 CSV。
- 多个实例共享同一个原型只导出一次；这里输出的是**原型及其后代**的尺寸，ACS/WCS 都属于该原型，不是顶层装配的实例坐标。匹配子装配后不再单独导出其子件，沿用原筛选语义。
- 已按本次用户要求改为逐节点卸载临时部件，保护用户原有完整加载/已修改/活动部件及递归祖先；详见下节。默认遍历深度仍为 5，可通过 `maxDepth` 调整。单个匹配大装配及 NX 缓存仍可能造成较高峰值，需实测。
- 当前取消为外部调用 `AssemblyTraverser.RequestStop()` 的协作式取消；仓库没有停止窗口，不承诺 ESC 可以终止阻塞的 NX API 调用。
- 每次输出到新的子目录，CSV 首五列兼容原字段，新增单位/坐标系/范围/实体数；名称转义、小数点固定、中文 UTF-8 BOM、PNG 文件名防冲突。
- 恢复截图隐藏 WCS/三重轴的原有特性，改用 View 局部开关并在 finally 恢复原值；不恢复原版强制开关为固定值的副作用，不执行源几何清理。

## 验证边界

本次已执行 DLX/XML/映射检查及 136 项独立 C# 断言。数学/文本/所有权策略之外，真实资源管理器、显示上下文、截图代码与显式 NX 测试替身组合编译并验证控制流；替身不证明官方 API 签名或原生行为。还以官方 net472 框架引用完成同一测试程序的编译，未在 Framework 4.7.2 运行。未执行 NX SDK 全量编译、NX 原生 DLX 加载、PNG 渲染、Teamcenter 实机测试或本地签名；这些由 [本地 agent 交接](../LOCAL_AGENT_PROMPT.md) 列出。纯算法测试通过不等于 NX 2512 集成验收通过。
