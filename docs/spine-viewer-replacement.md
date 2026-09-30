# 普通 SpineViewer 与剧情 Spine 替换

普通 SpineViewer 使用 v2 清单的 `type: "spine-assets"`。该入口覆盖由 MTI 或 Resources 加载、交给普通 `XX.SpineViewer` 使用的骨架，包括 Fatal 的预加载资源。旧版插件会拒绝此类型，更新 DLL 后需重启游戏。

主立绘继续使用 `type: "spine"` 与 `key + jsonKey`，保留污渍、预览和历史 JSON 的原有流程。主立绘仍只支持单页 atlas；本文的多页映射用于普通查看器。

## 获取地址

开启资源诊断并进入目标场景，在 `SpineViewer / ReplacementRuntime` 的记录中复制 `details.address`。地址包含实际文本加载器、容器、atlas 文本键和 JSON 键，区分大小写。

- MTI 的 `assetKey` 是实际文本容器，`atlasKey` 是 `MTISpine.atlas_key`，包含 `.atlas`；`jsonKey` 是实际选择的 JSON 键。
- Resources 的 `atlasKey`、`jsonKey` 是完整加载路径，例如 `SpineAnim/sample.atlas` 与 `SpineAnim/sample`，不填写 `assetKey`。
- 多个 JSON 可以共用同一 atlas。每个 JSON 单独声明目标，不用显示名或图片文件名推导其身份。

静态诊断入口 `SpineViewer.prepareAtlasAssetsS` 表示资源准备；普通查看器的 `viewer-source-bound` 表示实际消费者已登记。旧的独立 `draft1` 地址文件不会触发替换。

## 单页示例

`fatal_nusi_1` 使用 `fatal_nusi_0` 的 atlas 和图片容器，其 JSON 仍是 `fatal_nusi_1`：

```json
{
    "formatVersion": 2,
    "id": "my-fatal-nusi-1",
    "targets": [
        {
            "type": "spine-assets",
            "address": {
                "kind": "spine-assets",
                "loader": "mti",
                "assetKey": "Fatal/fatal_nusi_0.atlas",
                "atlasKey": "fatal_nusi_0.atlas",
                "jsonKey": "fatal_nusi_1"
            },
            "image": "candidate.png"
        }
    ]
}
```

仅替换图片时，PNG 必须匹配整张原 atlas 的尺寸和区域布局。可选 `atlas` 指向新的 atlas 文本；可选 `spine` 使用现有的 `json + replace` 分段合成规则，见[资源包参考](resource-packs.md)。未选择替换的骨骼、动画、事件和其他段保留原数据。

## 多页映射

`pages` 显式列出最终 atlas 中的全部页面，`pageKey` 必须逐字对应 atlas 页名，`image` 相对于清单所在目录：

```json
{
    "type": "spine-assets",
    "address": {
        "kind": "spine-assets",
        "loader": "mti",
        "assetKey": "Fatal/fatal_nusi_0.atlas",
        "atlasKey": "fatal_nusi_0.atlas",
        "jsonKey": "fatal_nusi_1"
    },
    "atlas": "candidate.atlas",
    "pages": [
        { "pageKey": "body.png", "image": "pages/body.png" },
        { "pageKey": "effects.png", "image": "pages/effects.png" }
    ]
}
```

页名示例仅说明格式，实际页名与区域布局由 `candidate.atlas` 决定。`image` 和 `pages` 不能同时填写。缺页、多余页、重复页名、跨页重复区域名、PNG 尺寸错误及不支持的 PMA 会拒绝该骨架候选。PNG 使用直通 alpha；每页保留独立材质和纹理绑定。

不提供图片时复用原页面纹理；如果新的 atlas 改了页名或尺寸，必须提供完整映射。多个包叠加时，JSON 段按原有顺序合成；最后一个声明 `image` 或 `pages` 的包提供整套图片映射，不跨包补齐缺页。每个明确引用的 PNG 都参与依赖检查和加密导出。

## 刷新与场景行为

1. 按通常方式启用资源替换和对应包。
2. 首次附加普通查看器资源时应用候选；修改文件后按 `Ctrl+T` 刷新。
3. 查看 `candidate-applied / viewer-pages-bound`，并核对实际场景。该结果不代表其他 JSON 或剧情画面已经验收。
4. 停用包或撤销授权后恢复原资源；查看器释放时清理插件创建的纹理、材质和骨架数据。

每个普通查看器使用独立替换资源，原 `MTISpine` 缓存保留，同屏骨架和共享 atlas 的不同 JSON 互不覆盖。刷新沿用 `AnimationState`、轨道对象、混合、排队动画和事件订阅，保留播放时间、皮肤组合及游戏设置的循环帧。材质从原材质复制，保持游戏的 shader、模板和混合设置。

坏候选会报告失败；仍获授权的已有候选可以继续显示。关闭时不会因为坏候选而保留未授权内容。替换骨架缺少正在播放或排队的动画时会拒绝更新，应通过兼容映射保留游戏所需的动画名。

普通查看器允许 `display.skeletonScale`。主立绘专用的偏移、显示尺寸、`scaleMultiplier`、`rightShift` 和 `effects.dirt` 不适用于该入口。既有主立绘预览不会自动跳转到剧情骨架。独立 PICT/EF_PICT 区域与 atlas-only 图片使用[图集替换入口](atlas-replacement.md)。同一图集同时启用该入口与本适配器的图片或 atlas 覆盖会被判为冲突；只改 JSON 且复用原页时可共享图集像素更新。
