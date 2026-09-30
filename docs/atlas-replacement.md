# 独立图集与剧情 PICT 图片替换

v2 清单支持 `type: "atlas-region"` 与 `type: "atlas-page"`，分别替换原图集中的一个区域或一整页。适用于已确认来源的 `SpineAtlasAsset` 和 Texture2D 页面，包括 Fatal 的 PICT / EF_PICT 图片；不要求区域被骨架附件引用，也不需要提供骨架 JSON。

更新插件 DLL 后重启游戏。主立绘的 SvTexture 污渍 RenderTexture 继续使用原有主立绘入口，不使用本文的区域目标。

## 定位原图集

开启[资源诊断](diagnostics.md)，正常进入目标场景，查找 `Atlas / ReplacementRuntime` 记录：

- `shared-page-bound`：页面已绑定，`details.address` 是可直接复制的整页地址。
- `picture-region-bound`：剧情实际查询了区域，`details.address` 是区域地址，`details.pageKey` 是所在页名。
- `candidate-applied / shared-atlas-texture-updated`：共享纹理的像素已更新；仍需检查目标画面。

地址区分大小写。MTI 的 `assetKey` 是 **atlas 文本容器**，`atlasKey` 是实际文本键，通常包含 `.atlas`，不是图片容器或导出 PNG 名。Resources 使用实际文本加载路径作为 `atlasKey`，不得填写 `assetKey`。来源必须由加载入口登记，不能只凭 `Texture.name` 或磁盘文件名推导。

## 区域替换

从原图集页制作一张**保持原尺寸、原布局的完整 PNG**。保留直通 alpha，不裁切、缩放、重新排布或取消打包旋转。插件仅复制目标区域的打包矩形，候选 PNG 中其他位置的像素不参与替换。

例如，`cuts_nightingale_0_0` 的三张 Noel 图片可以在同一个包中分别声明，并共用一张候选整页图片：

```json
{
    "formatVersion": 2,
    "id": "my-peeping-pictures",
    "targets": [
        {
            "type": "atlas-region",
            "address": {
                "kind": "atlas-region",
                "loader": "mti",
                "assetKey": "Fatal/cuts_nightingale_0_0.atlas",
                "atlasKey": "cuts_nightingale_0_0.atlas",
                "region": "noel_peeping__0000"
            },
            "image": "pictures-page.png"
        },
        {
            "type": "atlas-region",
            "address": {
                "kind": "atlas-region",
                "loader": "mti",
                "assetKey": "Fatal/cuts_nightingale_0_0.atlas",
                "atlasKey": "cuts_nightingale_0_0.atlas",
                "region": "noel_peeping__0001"
            },
            "image": "pictures-page.png"
        },
        {
            "type": "atlas-region",
            "address": {
                "kind": "atlas-region",
                "loader": "mti",
                "assetKey": "Fatal/cuts_nightingale_0_0.atlas",
                "atlasKey": "cuts_nightingale_0_0.atlas",
                "region": "noel_peeping__0002"
            },
            "image": "pictures-page.png"
        }
    ]
}
```

只改一张时保留对应目标即可。清单和 `pictures-page.png` 放入同一个资源包目录。每个目标仅接受 `type`、`address`、`image`；不填写 `jsonKey`、`spine`、`atlas` 或显示参数。原区域名、UV、旋转、透明裁边信息、位置和绘制材质继续由游戏管理。

## 整页替换

整页目标将候选 PNG 的全部像素写入原页面。PNG 尺寸仍须与原页一致；同页所有区域及持有这张纹理的消费者都会受影响。

下面使用 ver030g 原资源的实际页名。它是独立图集目标的字段示例，不代表已观察到游戏自然加载该资源；其他目标须从诊断复制原页名：

```json
{
    "type": "atlas-page",
    "address": {
        "kind": "atlas-page",
        "loader": "mti",
        "assetKey": "SpineAnim/damage_backvoreenemy.atlas",
        "atlasKey": "damage_backvoreenemy.atlas",
        "pageKey": "damage_backvoreenemy.png"
    },
    "image": "candidate-page.png"
}
```

将目标放入 v2 清单的 `targets` 数组。多页 atlas 可逐页声明，不要求替换全部页；每页分别使用其原始 `pageKey` 和尺寸。整页替换保留原图集的 180°、270° 等旋转信息，不按候选骨架的限制重新解释原布局。改变页布局或骨架数据属于[普通 SpineViewer 替换](spine-viewer-replacement.md)的用途，不在本入口修改。

## 共享、优先级与冲突

- 同一精确区域或页地址在不同包中重复时，按配置顺序使用最后一个匹配目标。
- 不重叠的区域可以组合；每次从原页重新合成，移除一个目标会恢复该区域，保留其他有效区域。
- 同一物理纹理出现整页与区域重叠，或不同地址覆盖相同像素时，拒绝该纹理的本次候选并恢复原像素，其他纹理继续处理。不会靠包顺序隐藏这种冲突。
- 原 atlas 中一个区域与其他区域共用像素时，区域替换会影响别名，因而拒绝该区域目标。确需整体修改时使用明确的整页目标。
- 同一图集不要同时启用 `spine-assets` 的 `image` / `pages` / `atlas` 覆盖，或旧 MTI 整图替换。检测到时拒绝图集候选；应在配置中选择一个图片入口。只改 JSON 且复用原页的 `spine-assets` 可共享本入口的像素更新。

Spine 和 PICT 若持有同一个原纹理对象，会同时看到修改。本入口保留纹理引用，不更换区域、材质或骨架，所以图层、遮挡与动画仍由游戏控制；并不为已自行复制纹理的外部消费者同步内容。

## 应用、刷新与排错

1. 按[安装与启用步骤](usage.md#安装启用和排序)放置清单和 PNG，启用资源替换及包 ID。
2. 正常进入目标场景。首次登记页面时应用候选；后续修改文件使用 `Ctrl+T`。
3. 查看诊断中的地址、页名和 `candidate-applied`，同时核对实际图片和同页其他内容。
4. 停用包或撤销 Sensitive 授权后恢复原像素；atlas/容器释放时清理持有的资源。

尺寸不符、未知区域/页、区域越界、PMA 或重叠目标会报告失败。坏候选不会留下一页混合成功的结果，失败页恢复原图。整页 PNG 不能使用导出的单张裁片代替。只有 `discovered` 时通常表示游戏尚未加载相应 atlas，或来源参数未匹配。

加密工具将 `image` 作为显式依赖，共用 PNG 只导出一次；明文、密文和 Sensitive 路径规则与其他 v2 包一致。Schema 和契约校验不读取游戏原 atlas，最终尺寸、区域与共享冲突检查在运行时完成。
