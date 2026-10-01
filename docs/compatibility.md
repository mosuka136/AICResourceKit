# 兼容性与支持范围

适用插件为 AICResourceKit 1.2.0，公共可安装契约版本为 2，游戏基线为 ver030h。清单支持 v2/v3；带立绘选择条件时使用 v3，密文使用 BEREENC v1；既有包无需改写或重新加密。机器可读能力由 [ResourceCapabilities](../AICResourceKit/Contracts/ResourceCapabilities.cs) 提供，签入版本见 [capabilities.json](resource-replacement/capabilities.json)。

## 资源类型

| 能力 | 可安装类型 | 边界 |
| --- | --- | --- |
| MTI 单图与直接图片 | `texture / mti` | 精确容器和图片键；保持原尺寸，保留 null/空键兼容语义 |
| Resources 图片与 Sprite | `texture / resources` | Texture2D、未打包 Sprite；保持纹理尺寸、网格与 UV；打包或旋转 Sprite 拒绝 |
| PXL 图片和页面 | `texture / pxl` | I/P 内嵌图片、外部和打包页；保持原帧布局，共享纹理冲突拒绝 |
| 主立绘 | `spine` | Spine 4.1、单页，支持分段组合、兼容映射和临时预览 |
| 立绘条件替换 | v3 的 `spine` / `texture / pxl` + `portraitSelection` | 主界面姿态、Spine 基础动画和状态；未命中时原版回退；不覆盖地图 PXL 动作或剧情 Spine，见[选择性替换](portrait-selection.md) |
| 普通剧情 Spine | `spine-assets` | MTI/Resources 来源，显式多页；独立查看器材质，保留播放状态 |
| 独立图集和 PICT | `atlas-region / atlas-page` | 保持图集布局；区域输入为完整页 PNG，只写选中矩形；重叠拒绝 |
| MPCC 调查 | 无替换类型 | 原生预设解码与已登记 PXL 输入映射；不应用预设、不重算调色纹理 |
| 视频 | 不支持 | 只提供加载观察，不接受可安装视频目标 |

资源关闭、包停用与 Sensitive 授权撤销恢复原资源，普通启用与排序合并连续变更。多个普通图片包按顺序覆盖，Spine 分段按顺序组合；同一物理纹理的跨 PXL/图集冲突不会被排序掩盖。具体行为见[资源状态](resource-status.md)与[资源契约](resource-contract.md)。

## ver030g 地址参考

以下地址取自 ver030g，作为历史示例保留，不代表已逐一在 ver030h 中验证。这些参数用于理解加载范围，制作新包时仍应从目标场景的[诊断报告](diagnostics.md)确认实际来源。

| 来源 | 已知参数及限制 |
| --- | --- |
| 标题角色与难度图片 | 容器 `MTI_title`，图片键 `key_noel`、`difficulty` |
| 历史主立绘 JSON | `key=stand_battle`、`jsonKey=stand_battle.old`；必须与普通 `stand_battle` 区分 |
| Noel 外部主纹理 | 旧 MTI 容器 `PxlNoel/noel.pxls.bytes.texture_0`，`imageKey=null`；图片、图层和材质共享纹理 |
| PXL 来源 | `PxlNoel/noel.pxls`、`PxlNoel/noel_bassrobe.pxls`、`MTI_mgm_ttr` 的 `_icons_ttr.pxls`、`Pxl/_icons.pxls`；图片和页面需按实际加载的角色及页目录定位 |
| Fatal 共享图集 | `fatal_nusi_1` 使用 `Fatal/fatal_nusi_0.atlas` 文本容器，图片容器/键为 `Fatal/fatal_nusi_0` / `fatal_nusi_0`；多个 JSON 可以共享一张图集 |
| 剧情 PICT | `noel_peeping__0000/0001/0002` 来自 `Fatal/cuts_nightingale_0_0.atlas`；PICT 与骨架共用纹理，区域也有默认皮肤附件引用 |
| MPCC 预设 | 原生读取器接受版本 0；NOEL 和 sub_i 的模式、部件及输入限制见[MPCC 说明](mpcc-inspection.md#ver030g-的五个文件) |

`MTI.resources_path` 按原始容器键构造，不能根据小写磁盘名推导其大小写。`Texture.name`、导出文件名和外部 PNG 前缀也不能替代实际参数。

## 如何确认兼容性

`capabilities` 描述编译版本支持什么；`inspect` 检查声明和文件；`resource-status.json` 描述当前登记消费者及应用结果；`resource-diagnostics.json` 记录实际入口和历史事件。应结合所需类型、实际地址和目标画面判断兼容性。
