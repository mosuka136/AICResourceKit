# 按姿态、动画与状态替换立绘

`portraitSelection` 用于只替换主界面立绘的一部分显示状态。例如，只替换 `stand_weak` 的 `weak` 动画且排除破衣，进入破衣时使用游戏原版。支持主立绘 `type: "spine"` 和主立绘使用的 `type: "texture", loader: "pxl"`；不用于地图人物的 PXL 动作或剧情 `spine-assets`。

需要 AICResourceKit 1.2.0 或更新版本，并将资源包的 `formatVersion` 设为 `3`。旧 v2 包继续使用原来的整套替换行为。更新插件 DLL 后重启游戏。

## 只替换 weak 的非破衣状态

将下面的清单保存为 `weak.replacement.json`，与候选文件放在同一资源包目录：

```json
{
    "formatVersion": 3,
    "id": "weak-normal-only",
    "targets": [
        {
            "type": "spine",
            "key": "stand_weak",
            "jsonKey": "stand_weak",
            "image": "weak-normal.png",
            "atlas": "weak-normal.atlas",
            "spine": {
                "json": "weak-normal.json",
                "replace": ["all"]
            },
            "portraitSelection": {
                "animations": ["weak"],
                "excludeStates": ["TORNED"]
            }
        }
    ]
}
```

游戏选择 `weak` 且没有 `TORNED` 时应用这份资源；进入破衣或切换到其他基础动画时，该目标不参与替换。返回匹配状态后自动再次应用。`weak` 所在的游戏姿态通常是 `STAND`，不要把资源键 `stand_weak` 当成 `poses` 的值。

候选 Spine 可以只包含选中状态需要的内容：

- JSON 是一份可独立解析的 Spine 4.1 骨架，包含当前状态实际需要的骨骼、插槽、附件、皮肤和动画。用 `replace: ["all"]` 安装这份状态专用骨架。
- atlas 和 PNG 只需覆盖该候选骨架引用的区域，不必提供未选中破衣状态的附件、贴图或 `torned` 动画。
- 游戏可能同时播放基础动画、表情和其他叠加轨道。选中状态所需的这些动画与皮肤仍须存在，可按[资源包参考](resource-packs.md)提供兼容映射。
- 不会自动裁剪 JSON 中保留的附件或合并缺失的 atlas 区域。若仍保留一个附件，它引用的区域必须存在。

只改 PNG 时可以省略 `atlas` 和 `spine`，继续使用原骨架和原 atlas；图片画布和区域布局必须与原图一致。被选中状态实际用到的区域不能留空，未选中状态独用的区域可以留空。

`TORNED` 是游戏状态标记；`torned` 是 Spine 叠加动画名。`animations` 匹配基础动画，不匹配叠加轨道，所以排除破衣应使用 `excludeStates`。

## 选择字段

所有字段均可省略，但 `portraitSelection` 至少包含一个条件。提供的数组必须非空，元素不能重复，名称区分大小写。

| 字段 | 匹配规则 | 示例 |
| --- | --- | --- |
| `poses` | 当前游戏立绘姿态命中任一名称 | `["STAND", "BENCH"]` |
| `animations` | 当前 Spine 基础动画命中任一名称；仅主立绘 Spine 可用 | `["weak"]` |
| `requireStates` | 列出的状态标记全部存在 | `["LOWHP", "LOWMP"]` |
| `excludeStates` | 列出的状态标记均不存在 | `["TORNED"]` |

不同字段之间为“且”。`NORMAL` 特指状态值为零，不表示“衣服完整”：低体力 `LOWHP`、低魔力 `LOWMP` 都不是 `NORMAL`。只排除破衣时不要增加 `requireStates: ["NORMAL"]`。

可用状态名：`NORMAL`、`DIRT`、`PROG0`、`PROG1`、`PROG2`、`LOWHP`、`BATTLE`、`SER`、`SHAMED`、`SMASH`、`ABSORBED`、`STUNNED`、`LOWMP`、`EGGED`、`WET`、`BOTE`、`ORGASM`、`CONFUSED`、`OSGM`、`TORNED`、`SLEEP`、`DEAD`、`STONEOVER`、`SP_SENSITIVE`。

匹配的是游戏最终用于绘制的姿态与状态，包括敏感内容设置对显示状态的调整。条件不改变角色的游戏状态或存档。附加状态 `EMSTATE_ADD` 暂不作为独立筛选字段。

## PXL 立绘

从[诊断报告](diagnostics.md)复制该立绘实际使用的 PXL `address`，沿用[PXL 替换说明](pxl-replacement.md)的图片或页面目标，并在目标中增加条件，例如：

```json
"portraitSelection": {
    "poses": ["DAMAGE_0"],
    "excludeStates": ["TORNED"]
}
```

同时把清单顶层改成 `"formatVersion": 3`。示例的 `DAMAGE_0` 需替换为实际采用 PXL 绘制的立绘姿态；如果游戏为该状态选择了 Spine 身体，应使用对应 Spine 目标。

候选 PNG 必须保持原页面的完整画布尺寸和区域布局。可以只绘制选中姿态/状态实际使用的区域，其他区域留透明；不能把选中区域裁成小图。条件命中时只给当前立绘材质绑定候选图片，不改写共享原纹理，其他姿态和共享消费者继续使用原图。不会替换 PXLS 的帧序、方向、图层结构或动作定义。

## 启用、检查与错误处理

1. 用[命令行工具](cli.md)的 `inspect` 检查清单和文件，将包放入 `ReplaceTexture/` 后启用。
2. 通过立绘控制页或自然游戏状态进入条件命中的姿态，确认候选显示正常。
3. 切换到排除状态，例如给 `weak` 增加 `TORNED`，确认原版仍能显示，再切回检查候选恢复。
4. 修改文件后使用“刷新资源”；停用包或关闭资源替换会恢复原资源。

新启用的条件 Spine/PXL 目标若已匹配当前实际姿态、基础动画和状态，直接按正常包顺序生效，不触发预览或重启动画；不匹配时才尝试主界面立绘自动预览。插件从游戏可用的姿态中选择符合 `poses`、`animations` 和状态条件的组合；例如当前为破衣状态、包只替换普通 `weak` 时，会临时切换到普通 `weak`。资源实际显示后预览约 2 秒；恢复资源准备完成后，一次切回此前的姿态、主状态、附加状态及控制页锁定。准备恢复时保持预览画面，不先切到原版 normal。

预览期间仅临时提高新启用目标的优先级，不改变包列表顺序。PXL 通过当前立绘实际使用的页或图片地址匹配；整页替换的普通 PXL 目标仍不触发立绘预览。找不到符合条件且游戏可显示的主立绘时跳过预览，可到对应场景或立绘控制页检查。候选缺少本次显示必需的轨道或皮肤时，结束预览并恢复此前的立绘。

所选 Spine 状态缺少必要动画/皮肤、PNG 尺寸错误或文件损坏时，会在[资源状态](resource-status.md)中报告失败。当前状态没有可用候选时继续显示原版；同一状态刷新时仍获授权的上一份有效 Spine 组合可能保留到新资源准备完成。

包的排序和身份规则不变。条件不匹配的目标不参与当前立绘选择；若另一个已启用包也替换该资源，它仍按通常优先级生效。要检查纯原版回退，应先停用其他覆盖相同资源的包。同一包不能通过多个不同条件重复声明同一目标身份；需要多份候选时使用不同包。
