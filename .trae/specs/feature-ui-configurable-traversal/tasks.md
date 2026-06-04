# Tasks

- [x] Task 1: 创建配置数据类 TraversalConfig.cs
  - [x] SubTask 1.1: 定义配置属性（filePath, namePatterns, idPatterns, flagImageOn, flagXYZOn, viewList, coordinateSelection）
  - [x] SubTask 1.2: 添加构造函数和属性访问器

- [x] Task 2: 修改 ImageExporter.cs 支持构造函数注入视图列表
  - [x] SubTask 2.1: 删除静态属性 CaptureViews
  - [x] SubTask 2.2: 添加私有成员变量存储视图列表
  - [x] SubTask 2.3: 修改构造函数接收视图列表参数
  - [x] SubTask 2.4: 修改 Process 方法使用实例视图列表

- [x] Task 3: 修改 BoundingBoxExporter.cs 支持构造函数注入坐标系
  - [x] SubTask 3.1: 删除静态属性 BoundingBoxCsysMode
  - [x] SubTask 3.2: 添加私有成员变量存储坐标系模式
  - [x] SubTask 3.3: 修改构造函数接收坐标系参数
  - [x] SubTask 3.4: 修改 GetBoundingBoxDimensions 方法使用实例坐标系

- [x] Task 4: 修改 AssemblyTraverser.cs 支持动态配置
  - [x] SubTask 4.1: 删除静态配置属性（OutputFolder, ValidNamePatterns, ValidIdPatterns, _transactionHandlers）
  - [x] SubTask 4.2: 修改 Main 方法接收 TraversalConfig 参数
  - [x] SubTask 4.3: 添加动态构建事务处理器数组的逻辑
  - [x] SubTask 4.4: 修改 TraverseAssembly 方法使用配置参数

- [x] Task 5: 更新 UIscripts.cs 集成配置界面
  - [x] SubTask 5.1: 添加 UI 控件（路径选择器、复选框、列表框、下拉框）
  - [x] SubTask 5.2: 实现 OnExecute 回调函数收集参数
  - [x] SubTask 5.3: 创建 TraversalConfig 并调用 AssemblyTraverser.Main

- [x] Task 6: 更新 README.md 添加 mermaid 流程图和配置说明
  - [x] SubTask 6.1: 添加项目架构流程图
  - [x] SubTask 6.2: 更新文件说明和配置说明章节
  - [x] SubTask 6.3: 添加使用方法（UI配置方式）

# Task Dependencies

- [Task 2] depends on [Task 1] (ImageExporter 需要使用 SnapViewType)
- [Task 3] depends on [Task 1] (BoundingBoxExporter 需要使用配置)
- [Task 4] depends on [Task 1], [Task 2], [Task 3] (AssemblyTraverser 需要配置类和修改后的处理器)
- [Task 5] depends on [Task 1], [Task 4] (UIscripts 需要配置类和修改后的主程序)
- [Task 6] depends on [Task 1-5] (README 需要反映所有改动)