# NX_Traverser
NXUG自动化程序遍历Teamcenter结构树

## 项目简介
这是一个基于NXOpen C# API开发的自动化工具，用于遍历装配结构树并执行多种操作，包括：
- 多视角截图导出（8个标准视角）
- 包容体尺寸测量与CSV导出

## 文件说明

| 文件名 | 功能描述 |
|--------|----------|
| [AssemblyTraverser.cs](file:///workspace/AssemblyTraverser.cs) | 主程序，负责装配结构树遍历、过滤和事务调度 |
| [AssemblyTraverser.Utilities.cs](file:///workspace/AssemblyTraverser.Utilities.cs) | 工具类，提供名称获取和通配符匹配功能 |
| [BoundingBoxExporter.cs](file:///workspace/BoundingBoxExporter.cs) | 包容体尺寸导出器，支持ACS/WCS坐标系，导出CSV格式结果 |
| [ImageExporter.cs](file:///workspace/ImageExporter.cs) | 截图导出器，支持8个标准视角的PNG截图 |
| [ITransactionHandler.cs](file:///workspace/ITransactionHandler.cs) | 事务处理器接口，可扩展新功能 |
| [StopForm.cs](file:///workspace/StopForm.cs) | 停止控制窗体 |

## 主要功能

### 1. 装配结构遍历
- 深度优先递归遍历装配结构树
- 支持最大层级限制
- 支持组件名称/ID通配符过滤
- 自动按需加载组件

### 2. 多视角截图
- 8个标准视角：正等测、斜等测、顶、前、右、后、底、左
- 透明背景PNG格式
- 自动增强边缘

### 3. 包容体测量
- 支持ACS（绝对坐标系）和WCS（工作坐标系）
- 精确包围盒计算
- CSV格式导出（零件名,ID,x,y,z）
- 自动过滤异常实体（>1e6尺寸）

## 配置说明

### AssemblyTraverser.cs 配置
- `OutputFolder`: 输出文件夹路径
- `MaxLevel`: 最大处理层数
- `ValidNamePatterns`: 组件名称通配符过滤
- `ValidIdPatterns`: 组件ID通配符过滤
- `_transactionHandlers`: 启用的事务处理器数组

### BoundingBoxExporter.cs 配置
- `CsvFileName`: CSV文件名
- `BoundingBoxCsysMode`: 坐标系模式（"ACS"或"WCS"）

### ImageExporter.cs 配置
- `CaptureViews`: 截图视角数组

## 使用方法
1. 在NX中打开装配体
2. 运行本程序
3. 程序会自动遍历结构树并执行配置的操作
4. 结果保存到配置的输出文件夹
