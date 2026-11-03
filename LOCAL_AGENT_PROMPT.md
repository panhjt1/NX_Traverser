# 本地 agent：NX 2512 编译、验收与部署提示词

更新时间：2026-11-03。目标分支：`fix/nx2512-teamcenter-readonly`；修改基线：`2470a54`。这是本仓库的持续交接文档，后续每次代码、DLX 或构建配置变更都必须同步维护。

## 任务与边界

你在 Windows + Siemens NX 2512 + Teamcenter 集成环境工作。请完成下面的 SDK 编译、签名、实机验证，再部署测试版本。先阅读 [官方 API 核对与审查](docs/NX2512_API_REVIEW.md)。云端已经修改源码和资产，但**没有声称 NX 编译或 Teamcenter 无弹窗验收通过**。

不要为了通过测试而签出源部件、保存修改、修改发布状态/ACL、清除 IsModified、删除用户部件、屏蔽全部 NX 对话框。不要把 1946 的 DLL 混入 2512 引用。保留本地已有未提交修改；拉分支发生冲突时先检查并解决，不使用 reset --hard。

## 取代码

```powershell
git fetch origin
git switch --track origin/fix/nx2512-teamcenter-readonly
```

如果该分支已存在，使用 `git switch fix/nx2512-teamcenter-readonly`，然后 `git pull --ff-only`。源码和说明都在 GitHub，取代码不需要 OpenAI 云服务。

## 本次改动

| 文件 | 变更与本地关注点 |
|---|---|
| `ClassLibrary1.dlx`、`.gitignore` | XML 声明前空行修复，XML 格式化，移除个人绝对路径，改 OK/Cancel，DLX 纳入跟踪；保留全部 9 个控件。`NX="1946"` 保留真实生成器来源，需 2512 Styler 验证另存，不能仅把数字改成 2512 |
| `UIscripts.cs` | 已弃用 Show 改 Launch；检查 Ok 返回值；只在本次 OK 成功后保存配置；Dispose 对话框后运行；Apply 只验证，Cancel 不执行；释放 PropertyList；按 DLL 所在目录优先寻找配套 DLX |
| `TraversalConfig.cs`、`ExportUtilities.cs` | 输入验证、空白过滤、中文 ACS/WCS 映射；`maxDepth` 默认 5；CSV 引号转义/固定小数点；安全且防冲突的 Windows 文件名 |
| `AssemblyTraverser.cs`、`NxGeometry.cs` | 新建每次运行目录；按需加载并检查/释放状态；检查 OpenComponentStatus；跳过抑制项；相同原型只处理一次；匹配装配后不单独导出后代；删除源部件 Close；事务独立捕获错误，释放资源；防止重入 |
| `NxPartContext.cs` | 使用 SetActiveDisplay 建立目标原型上下文；恢复原显示部件及精确工作组件，PreserveRefset；恢复失败停止遍历。官方说明显示切换可能重置 undo 标记，不能承诺恢复撤销历史 |
| `ImageExporter.cs` | 删除无效 UFSession.UI/AskSuppressDialogs 和反射常量；删除 WCS 可见性写入、固定三轴设置、源几何清理；保留已有 WCS/三轴可见性，恢复原视图；Builder 在 finally 销毁；恢复失败停止遍历；中途取消不计为完整截图事务 |
| `BoundingBoxExporter.cs`、`BoundingBoxMath.cs` | 官方确认 Tag.Null 表示当前 WCS，不是 ACS。ACS 使用临时单位 CSYS；WCS 直接查询该方向的精确盒；按 UF 方向向量聚合八角点；装配后代使用 occurrence tag；临时对象 finally 清理；失败时不写偏小的尺寸 |
| `RunLog.cs` | `Traversal.log` 同步 Listing Window，含阶段、只读/修改状态、NX 错误码；检测加载后新增修改标记；不清除修改标记 |
| `NX_Traverser.csproj`、`scripts/build.ps1` | 新增 net48 / x64 构建；使用当前安装的 NXBIN/managed；Siemens 引用 Copy Local=false；嵌入本机 NXSigningResource.res；复制 DLX；输出构建日志和 SHA256 |
| `tests/` | 独立 C# 核心回归和 XML/控件一致性验证，不需要 NX SDK；不替代实机测试 |

## 构建与签名

1. 记录实际 NX 2512 maintenance build、Teamcenter 版本、NX 启动方式、VS/MSBuild 版本。确认运行中的 NX 与引用 DLL 来自同一 2512 安装。
2. 需要 Visual Studio 2022（或 Build Tools，含 .NET 桌面构建）和 **.NET Framework 4.8 Developer Pack**。本插件用 Framework 4.8，不是 net8；不要引用 `managed_core`。
3. 使用 **NX 2512 Block UI Styler** 打开 `ClassLibrary1.dlx`。确认中文标签、9 个 ID、八个英文视角条目、多选视角和 OK/Cancel。若工具要求升级另存，执行后提交该 DLX；不要生成模板覆盖 `UIscripts.cs`。XML 能解析不代表原生 Styler 验证通过。
4. 编译，安装路径以本机实际位置为准：

```powershell
.\scripts\build.ps1 -NXInstallDir 'C:\Program Files\Siemens\NX2512' -Configuration Release
```

输出应为 `bin/Release/NX_Traverser.dll` 与 `ClassLibrary1.dlx`，以及 PDB。`artifacts/build.binlog` 留在本地。目录内不应有任何 `NXOpen*.dll` 或 `ManagedLoader.dll`。记录全部警告，特别是 Obsolete、引用版本和平台警告；发现 API 不符时先查本机官方 XML 文档/2512 .NET Reference，再修代码，不使用反射猜测补丁。

5. 对无 NX Open Author 许可的使用者，应按站点授权方式签名。官方签名资源已加入项目，用 2512 安装中的工具（先在本机检查帮助/参数）：

```powershell
$nxInstall = 'C:\Program Files\Siemens\NX2512'
& "$nxInstall\NXBIN\SignDotNet.exe" '.\bin\Release\NX_Traverser.dll'
& "$nxInstall\NXBIN\SignDotNet.exe" -verify '.\bin\Release\NX_Traverser.dll'
```

签名会改变 DLL，**签名后**再次记录 SHA256。不要将 Siemens DLL、资源文件、许可或公司部件提交到公开仓库。
6. 部署到站点测试用自定义 `application` 目录，DLL/DLX 必须配套。通过 NX 的“文件 → 执行 → NX Open/Designcenter Open”启动 DLL，入口 `ClassLibrary1.Main`；它不是可单独播放的单文件 Journal。若采用现有菜单启动方式，先验证对应卸载策略；当前 `Immediately` 用于手动执行。

## 验证步骤与预期

先用**独立测试会话**，记录运行前 display/work part、work component、reference set、模型修改标记、WCS 和视图状态。不要在带有重要未保存编辑的生产会话直接验证显示切换。

### 离线检查

```powershell
python .\tests\validate_dlx.py
dotnet run --project .\tests\CoreTests.csproj -c Release
```

第二条需要 .NET 8 SDK，仅用于独立回归程序，不改变插件 net48 目标，也不需要 NuGet 包。没有 Python/SDK 时可先完成 NX 编译，但应在交接结果中记录未运行项。

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
- 确认未触发本工具的保存/签出/关闭；用户原先加载的部件仍在会话中。因不自动卸载，大装配运行中观察内存。
- 当前 display 与 work 不同、原 work 是重复实例中的第二个：完成及失败后必须恢复同一个 work occurrence，而不只是同一 Part。
- 输出目录不可写、CSV 被 Excel 占用、图片导出失败、加载失败时：异常有日志、Builder/CSV/加载状态释放、源部件上下文恢复。资源恢复失败应停止后续处理。
- 若仍有只读提示，先依据阶段隔离测试显示切换、加载、Orient 和临时 CSYS。站点回调或旧部件转换可能是剩余原因，不能宣称通过“代码不调用 Save”就完全排除弹窗。

## 当前已验证及未验证

- 云端 **28 项 C# 核心断言通过**，使用 .NET 8.0.427 SDK 的 Roslyn 编译并执行真实的 `BoundingBoxMath.cs`、`ExportUtilities.cs`。
- `python tests/validate_dlx.py` 通过；`git diff --check` 通过。
- 对比旧 DLX 解析树，除文件标题、日期尾部空白、OK/Cancel 导航外，原属性均保留。12 个插件源文件通过 C# 7.3 语法解析；这不包含 NX SDK 类型检查。
- 已读取并核对官方 2512 API 链接，见 `docs/NX2512_API_REVIEW.md`。
- **待本地**：NX SDK 编译/链接、签名、原生 DLX 打开、视图恢复精度、PNG、临时 CSYS 的只读行为、Teamcenter 全部实测。这里没有 NX 运行时，不能将离线检查替代这些结果。

## 回传并持续维护

完成后给出：分支/提交、NX/TC/VS 版本、构建与签名结果、DLL/DLX 的最终 SHA256、部署路径、每项验收通过/失败/未测、脱敏日志和复现步骤。若需修代码，提交到同一分支并在本文件更新“改动、验证状态、剩余工作”，也更新 API 依据。不要只交付本机 DLL 而不回传对应源码。
