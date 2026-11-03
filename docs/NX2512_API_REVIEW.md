# NX 2512 官方 API 核对与审查记录

核对日期：2026-11-03。基线：`2470a54`。所有下列依据均来自 **Siemens 官方 2512 文档**，不是把旧版社区示例视为新版本 API。官方文档浏览器页面动态加载，本文链接到同一文档的正文或参考类。

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
| `SetActiveDisplay`、工作组件恢复 | [PartCollection](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a70134.html) | 用 `ReplaceExisting` + `SameAsDisplay` 建立原型上下文；恢复原 display/work occurrence，使用 `PreserveRefset`；释放加载状态 |
| 按需加载/只读诊断 | [BasePart](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a38266.html)、[ComponentAssembly](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a37866.html) | `OpenComponents(ComponentOnly)`、`LoadThisPartPartially/Fully`；检查加载结果；使用 `IsReadOnly/IsModified` 记录状态 |
| 包围盒坐标含义 | [UF_MODL_ask_bounding_box_exact](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_modl/global.html#UF_MODL_ask_bounding_box_exact)、[UFModl .NET](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a79234.html) | `Tag.Null` 表示当前 WCS；CSYS 必须位于当前 work part 上下文；八角点必须使用方向向量，不可直接 `min + distances` |
| ACS/WCS | [UFCsys](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a77982.html)、[UF_CSYS](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_csys/global.html)、[WCS](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a82066.html) | ACS 用临时单位矩阵 CSYS；WCS 使用目标原型的 `WCS.CoordinateSystem.Tag`，直接请求按该坐标系对齐的精确包围盒 |
| 临时对象清理 | [UF_OBJ_delete_object](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/ugopen_doc/uf_obj/global.html#UF_OBJ_delete_object) | 只删除本次创建的临时 CSYS/矩阵，不修改、替换或删除源 WCS |
| 装配体测量 | [Component.FindOccurrence](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a37862.html) 及上述 UF_MODL 文档 | 后代使用 occurrence tag 取得装配空间的包围盒；叶子使用原型 tag；参考集不含实体导致无法取得 occurrence 时明确失败 |
| 视图和图片 | [View](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a81942.html)、[ImageExportBuilder](https://docs.sw.siemens.com/documentation/external/PL20250429951538534/en-US/custom_api/nxopen_net/a61974.html) | 保留有文档的 `Orient`、`SetRotationTranslationScale`、PNG/Transparent/EnhanceEdges；Builder 在 finally 销毁 |

## Teamcenter 只读弹窗：确定的代码行为与待实测原因

**源码已证实**：旧版逐图切换 `part.WCS.Visibility`，把三轴显示强制改成固定值，执行 `CleanUpFacetedFacesAndEdges`，遍历后主动 `Close(...UseResponses, null)`，并且只读抑制代码本身包含无效 API。它还在 Block Styler 对话框对象尚未释放时执行导出。

**诊断推断**：这些写入/清理/关闭操作遇到只读或已修改的受管部件时，是弹窗的可疑触发点；未连接实际站点，不能断言它们就是每个弹窗的唯一原因。本分支删除这些操作，并在对话框释放后执行。不会签出、保存、改变 Teamcenter ACL、修改发布状态或清除源部件修改标记。

**保留的必要操作**：按需加载、切换显示/工作上下文、临时坐标系、屏幕视图旋转。加载旧版本零件、站点自定义回调和显示切换本身仍可能产生提示或修改标记。官方还说明 `SetActiveDisplay` 可能重置撤销标记列表；恢复 display/work/view 不等于恢复 undo 历史。本地应先在独立测试会话验收。

若仍弹窗，用 `Traversal.log` 最后一条 `[Load geometry]` / `[Context]` / `[BoundingBox]` / `[Orient]` / `[Restore view]` 定位阶段，记录 NX 错误号、弹窗完整文字、2512 maintenance build、该对象是否已发布/被他人签出、当前 work/display 状态及 NX syslog。不要直接加猜测的 suppress API 或强行签出来掩盖问题。

## 其他审查修复及边界

- DLX XML 声明前空行使 XML 无法严格解析；`.gitignore` 也错误忽略 DLX。已修复。`NX="1946"` 保留真实生成器来源，需本地 Styler 验证/另存，不能只改数字冒充格式升级。
- 中文 WCS 选项原先落入 ACS 默认分支；现在显式映射，不接受未知值。
- 原 WCS 算法旋转的是 ACS 外包围盒，不是实体，会高估；原 ACS 又错误依赖当前 WCS。已直接按目标坐标系查询并正确聚合方向向量。
- 不再用坐标绝对值大于 1e6 的规则静默过滤实体；拒绝 NaN/Infinity/负尺寸；任一实体失败则不写不完整 CSV。
- 多个实例共享同一个原型只导出一次；这里输出的是**原型及其后代**的尺寸，ACS/WCS 都属于该原型，不是顶层装配的实例坐标。匹配子装配后不再单独导出其子件，沿用原筛选语义。
- 不主动关闭加载的部件，以免关闭用户已有部件/破坏共享原型；代价是大型装配的会话内存增长。默认遍历深度仍为 5；可通过配置 `maxDepth` 调整，遇到上限会记录跳过。
- 当前取消为外部调用 `AssemblyTraverser.RequestStop()` 的协作式取消；仓库没有停止窗口，不承诺 ESC 可以终止阻塞的 NX API 调用。
- 每次输出到新的子目录，CSV 首五列兼容原字段，新增单位/坐标系/范围/实体数；名称转义、小数点固定、中文 UTF-8 BOM、PNG 文件名防冲突。
- 为减少源部件状态修改，图片保留当前 WCS/三轴可见性；需要无坐标轴截图时，在用户可控的会话设置中预先调整并做本地验证。

## 验证边界

本次已执行 DLX/XML/映射检查及不依赖 NX 的 C# 数学/文本回归检查。未执行 NX SDK 全量编译、NX 原生 DLX 加载、PNG 渲染、Teamcenter 实机测试或本地签名；这些由 [本地 agent 交接](../LOCAL_AGENT_PROMPT.md) 列出。纯算法测试通过不等于 NX 2512 集成验收通过。
