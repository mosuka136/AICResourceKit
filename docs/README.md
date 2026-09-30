# AICResourceKit 文档

本目录维护当前代码对应的正式使用与开发文档。命令默认在仓库根目录执行；标为游戏目录的路径相对于游戏安装目录。

## 按任务查阅

| 读者与任务 | 首选文档 | 补充参考 |
| --- | --- | --- |
| 玩家：安装、启用资源包、调整立绘 | [使用说明](usage.md) | [从 BetterExperience 迁移](migration.md) |
| 资源包作者：制作、校验与加密发布 | [使用说明](usage.md) | [资源包参考](resource-packs.md) |
| PXL 作者：定位内嵌图片与整页 | [PXL 替换](pxl-replacement.md) | [资源契约](resource-contract.md) |
| 剧情 Spine 作者：共享图集与多页替换 | [普通 SpineViewer 替换](spine-viewer-replacement.md) | [资源包参考](resource-packs.md) |
| 图集作者：独立区域、整页和 PICT 图片 | [图集替换](atlas-replacement.md) | [资源契约](resource-contract.md) |
| 调查者：MPCC 调色文件、部件与图片映射 | [MPCC 调查](mpcc-inspection.md) | [逐项记录](resource-replacement/ver030g-mpcc-investigation.json) |
| 玩家与作者：查看当前加载结果和文件错误 | [资源状态](resource-status.md) | [日常操作](usage.md) |
| 调查者：查实际加载参数和失败原因 | [资源加载诊断](diagnostics.md) | [ver030g 调查清单](resource-replacement/ver030g-investigation.json) |
| 工具开发者：解析清单与匹配目标 | [资源契约](resource-contract.md) | [API 调用示例](usage.md) |
| 下游制作工具：对接第一阶段交付 | [接口交付说明](phase-one-delivery.md) | [编译能力](resource-replacement/capabilities.json)、[验证记录](resource-replacement/phase-one-verification.json) |
| 项目维护者：构建、修改与测试 | [开发说明](development.md) | [资源工具计划](planning/noel-resource-tools.md) |

## 格式与能力状态

- 插件 1.1.0 的可安装契约已定版为契约 1；编译支持范围与实际验证范围分别记录，见[第一阶段交付](phase-one-delivery.md)。
- 可安装资源包使用 v2 `.replacement.json`。既有 BEREENC v1 密文仍兼容。
- [作者 Schema](resource-replacement/resource-replacement.schema.json)与[正反例](resource-replacement/contract-vectors.json)用于字段和语义校验。
- [地址草案样例](resource-replacement/address-draft.examples.json)只用于开发验证，不能直接安装到游戏。PXL 图片与页地址已通过 v2 `loader: pxl` 接入，普通 Spine 地址通过 `type: spine-assets` 接入；独立图集地址通过 `type: atlas-region` / `atlas-page` 接入。MPCC 已有原生读取报告与现有 PXL 地址映射，不新增二进制替换类型；视频替换暂缓，尚不支持安装。
- 调查清单说明来源和静态证据；诊断命中及测试通过都不等于画面验收。查看单项能力时应同时阅读它的限制。
- `planning/` 保存未来设计和实施顺序，不能作为当前支持功能的依据。

## 维护原则

功能、配置、命令或限制发生变化时，在同一次修改中更新对应文档和示例。字段定义以资源契约为统一参考，操作步骤以使用说明为统一入口，避免复制多份正文。文档与代码共同纳入 Git。

校验和测试只覆盖必要规则与常见场景。具体维护流程见[开发说明](development.md)。
