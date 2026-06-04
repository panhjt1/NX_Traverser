# Checklist

## 配置数据类
- [x] TraversalConfig.cs 已创建，包含所有必需属性（filePath, namePatterns, idPatterns, flagImageOn, flagXYZOn, viewList, coordinateSelection）
- [x] TraversalConfig 提供属性访问器和构造函数

## ImageExporter 修改
- [x] 静态属性 CaptureViews 已删除
- [x] 添加私有成员变量 _captureViews
- [x] 构造函数接收 SnapViewType[] 参数
- [x] Process 方法使用实例视图列表而非静态配置

## BoundingBoxExporter 修改
- [x] 静态属性 BoundingBoxCsysMode 已删除
- [x] 添加私有成员变量 _csysMode
- [x] 构造函数接收 string 参数
- [x] GetBoundingBoxDimensions 方法使用实例坐标系而非静态配置

## AssemblyTraverser 修改
- [x] 静态配置属性已删除（OutputFolder, ValidNamePatterns, ValidIdPatterns）
- [x] Main 方法签名改为接收 TraversalConfig 参数
- [x] 动态构建事务处理器数组逻辑已实现
- [x] TraverseAssembly 方法使用配置参数

## UIscripts 修改
- [x] UI 控件已添加（路径选择器、截图复选框、测量复选框、视图列表、坐标系下拉框）
- [x] OnExecute 回调函数已实现参数收集逻辑
- [x] 创建 TraversalConfig 实例并调用 AssemblyTraverser.Main

## README 更新
- [x] 添加项目架构 mermaid 流程图
- [x] 更新文件说明表格
- [x] 更新配置说明章节
- [x] 添加 UI 使用方法说明

## 代码规范
- [x] 所有修改遵循现有代码风格
- [x] 无编译错误
- [x] 无未使用的变量或引用