# AICResourceKit 文档

本文档描述当前插件和工具的使用方法、接口及维护规范。命令默认从仓库根目录执行；标为游戏目录的路径相对于游戏安装目录。

## 使用指南

| 内容 | 文档 |
| --- | --- |
| 安装、配置、最小资源包、启用与排序 | [使用说明](usage.md) |
| 主立绘分段合成、兼容映射与显示参数 | [资源包参考](resource-packs.md) |
| 只替换指定立绘姿态、动画和状态 | [选择性替换](portrait-selection.md) |
| PXL 内嵌图片、外部页与打包页 | [PXL 替换](pxl-replacement.md) |
| 普通 SpineViewer、共享 atlas 与多页 | [剧情 Spine 替换](spine-viewer-replacement.md) |
| 独立区域、整页与 PICT 图片 | [图集替换](atlas-replacement.md) |
| 检查资源包、查询能力与加密发布 | [命令行工具](cli.md) |

## 状态与诊断

| 内容 | 文档 |
| --- | --- |
| 当前加载结果、刷新与文件错误 | [资源状态](resource-status.md) |
| 实际加载参数、入口和消费者事件 | [资源加载诊断](diagnostics.md) |
| MPCC 调色预设与 PXL 输入映射 | [MPCC 调查](mpcc-inspection.md) |
| 支持类型、基线地址与已知限制 | [兼容性与支持范围](compatibility.md) |

## 接口与开发

| 内容 | 文档或文件 |
| --- | --- |
| 清单字段、目标身份、依赖及错误规则 | [资源契约](resource-contract.md) |
| 复用公共解析、版本判断与报告 | [工具集成](integration.md) |
| 作者字段形状与规范写法 | [Schema](resource-replacement/resource-replacement.schema.json) |
| 解析正反例、身份与依赖预期 | [测试向量](resource-replacement/contract-vectors.json) |
| 当前编译版本的机器可读支持范围 | [能力表](resource-replacement/capabilities.json) |
| 引用程序集、模块职责、测试与发布 | [开发说明](development.md) |

## 维护约定

操作步骤以使用指南为入口，字段语义以资源契约为准，支持范围和限制统一维护在兼容性说明中。文档、Schema、能力表和示例随代码同步更新，避免复制多份字段定义或过期的命令。

编译能力、静态文件检查、运行时观察和实际画面各有不同含义。原始调查材料、个人日志和构建产物不属于正式文档。
