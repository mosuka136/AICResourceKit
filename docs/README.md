# AICResourceKit 文档

本目录维护当前代码对应的正式使用与开发文档。命令默认在仓库根目录执行；标为游戏目录的路径相对于游戏安装目录。

## 按任务查阅

| 读者与任务 | 首选文档 | 补充参考 |
| --- | --- | --- |
| 玩家：安装、启用资源包、调整立绘 | [使用说明](usage.md) | [从 BetterExperience 迁移](migration.md) |
| 资源包作者：制作、校验与加密发布 | [使用说明](usage.md) | [资源包参考](resource-packs.md) |
| PXL 作者：定位内嵌图片与整页 | [PXL 替换](pxl-replacement.md) | [资源契约](resource-contract.md) |
| 调查者：查实际加载参数和失败原因 | [资源加载诊断](diagnostics.md) | [ver030g 调查清单](resource-replacement/ver030g-investigation.json) |
| 工具开发者：解析清单与匹配目标 | [资源契约](resource-contract.md) | [API 调用示例](usage.md) |
| 项目维护者：构建、修改与测试 | [开发说明](development.md) | [资源工具计划](planning/noel-resource-tools.md) |

## 格式与能力状态

- 可安装资源包使用 v2 `.replacement.json`。既有 BEREENC v1 密文仍兼容。
- [作者 Schema](resource-replacement/resource-replacement.schema.json)与[正反例](resource-replacement/contract-vectors.json)用于字段和语义校验。
- [地址草案样例](resource-replacement/address-draft.examples.json)只用于开发验证，不能直接安装到游戏。PXL 图片与页地址已通过 v2 `loader: pxl` 接入，其他新入口继续按计划实现。
- 调查清单说明来源和静态证据；诊断命中及测试通过都不等于画面验收。查看单项能力时应同时阅读它的限制。
- `planning/` 保存未来设计和实施顺序，不能作为当前支持功能的依据。

## 维护原则

功能、配置、命令或限制发生变化时，在同一次修改中更新对应文档和示例。字段定义以资源契约为统一参考，操作步骤以使用说明为统一入口，避免复制多份正文。文档与代码共同纳入 Git。

校验和测试只覆盖必要规则与常见场景。具体维护流程见[开发说明](development.md)。
