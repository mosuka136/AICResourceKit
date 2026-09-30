# PXL 图片与整页替换

PXL 目标使用 v2 清单中的 `type: "texture"`、`loader: "pxl"`、`address` 和 `image`。旧版插件不识别此加载器；更新 DLL 后需要重启游戏。

此入口只替换图片像素，不改 PXLS 的帧序、方向、图层、坐标、变换、时长或部件调色数据。候选 PNG 必须与实际纹理的整张画布尺寸一致，并保持原区域布局和透明度约定。

主界面 PXL 立绘可在 v3 清单中增加 `portraitSelection`，只给命中姿态/状态的立绘绑定新图片，未命中时使用原图。制作步骤和留空区域要求见[选择性替换](portrait-selection.md)。

## 查找实际地址

1. 在启动游戏前开启资源诊断，将筛选设为 `pxl` 或对应容器键。
2. 进入使用目标资源的场景。
3. 在 `logs/resource-diagnostics.json` 中找到 `PXL image/page / ReplacementRuntime` 的 `entry-hit`。`details.address` 是可复制的实际地址，`width` 和 `height` 是候选图片必须匹配的尺寸。
4. 将该地址放入目标的 `address`，指定候选 PNG，按通常方式启用资源包。

来源为读取 PXLS 时的 MTI `assetKey` 和 `textKey`。键区分大小写，不根据磁盘小写文件名、角色显示名、导出文件名或 `external_png_header` 推断。通过 `MTI.LoadBytes` 或 `MTI.Load<TextAsset>` 读取并交给 PxlsLoader 的来源均可跟踪；尚未提供 Resources 或自定义字节来源的可安装地址。

未命中相应加载路径时，不能从静态文件列表编造运行时地址。诊断开启前已登记的图片会列为 `discovered / registered-image-or-page`，不补写历史调用。

## 外部页

`pageIndex` 是 `PxlCharacter` 外部纹理数组的零基槽位。下面是 `noel.pxls` 外部主纹理的定位方式：

```json
{
    "formatVersion": 2,
    "id": "my-noel-pxl",
    "targets": [
        {
            "type": "texture",
            "loader": "pxl",
            "address": {
                "kind": "pxl-page",
                "source": {
                    "loader": "mti",
                    "assetKey": "PxlNoel/noel.pxls",
                    "textKey": "noel.pxls"
                },
                "storage": "external",
                "pageIndex": 0
            },
            "image": "noel-page-0.png"
        }
    ]
}
```

额外页分别声明目标，使用诊断中实际出现的槽位。不要因为主纹理可以替换就假定第 1 页或其他页一定存在。异步图片到达时自动登记并应用；目标页尚未取得纹理时会记录 `page-awaiting-texture`。

旧 `loader: "mti"` 主纹理定位仍保留。对同一纹理只启用一种定位方式；同时启用旧 MTI 目标与新的 PXL 目标会报冲突，避免两个更新入口互相覆盖。

## 打包页

当 PXLS 通过 `%PACK_SECTION%` 组织图片时，可以替换整页，保留全部区域与 UV。使用如下地址结构，其数值必须来自实际诊断：

```json
{
    "kind": "pxl-page",
    "source": {
        "loader": "mti",
        "assetKey": "Pxl/_icons.pxls",
        "textKey": "_icons.pxls"
    },
    "storage": "packed",
    "pageOrdinal": 0,
    "imageType": 0
}
```

`pageOrdinal` 是游戏解码的打包页数组序号；`imageType` 为 `0`（普通页）、`1`（parts 页）或 `2`（简化 parts 页）。二者共同定位页面。它们与外部数组的 `pageIndex` 含义不同，不应相互换算。

同一纹理可能同时有外部页和打包页地址，这些是同一张图的别名。一个包只能选择其中一个地址；在运行时发现同包通过多个别名重复覆盖时，会拒绝该纹理。不同包仍按配置顺序选择最后一个匹配目标。

## 独立内嵌图片

`%IMGS_SECTION%` 内的独立图片使用 `kind: "pxl-image"`，按原始 `imageId`、`imageId2` 和 `role` 定位：

```json
{
    "kind": "pxl-image",
    "source": {
        "loader": "mti",
        "assetKey": "PxlNoel/noel_bassrobe.pxls",
        "textKey": "noel_bassrobe.pxls"
    },
    "imageId": "123",
    "imageId2": "456.5",
    "role": "I"
}
```

上述 ID 仅展示字段格式，必须替换为诊断报告的真实值。`imageId` 是 UInt32 十进制字符串；`imageId2` 是原始 double 的可往返字符串，不能截断为整数或单精度。`role` 只能为 `I` 或 `P`，分别对应游戏的两个图片字段；P 不等同于所有资源的通用遮罩。

该地址只定位独立内嵌图片。打包页中的区域应使用整页地址；不能把区域 ID 当作可独立替换的整张 PNG。`%IMGV_SECTION%` 仍交给游戏解码，纯矢量数据不因此变成可替换 PNG。

## 刷新、关闭与错误处理

- 修改 PNG 或清单后按 `Ctrl+T`。刷新仍使用原纹理对象，共享它的帧、图层和缓存材质取得一致内容。
- 停用包或撤销 Sensitive 授权时恢复原像素。释放 PXL 时清理插件保存的原像素和任务，不销毁游戏拥有的原纹理。
- 更新保留尺寸、采样属性、颜色空间、mipmap 使用及原来的可读状态。PNG 通过 Unity 原生入口上传，像素格式可能随 PNG 改变；格式一致时直接恢复原始纹理数据，避免重复压缩原图。仅对正在替换的纹理保留一份用于关闭恢复的原像素。
- 一个包可包含多张图片和多页目标。缺失依赖或尺寸错误按目标报错；其他独立纹理继续处理，同一共享纹理的冲突不会被静默忽略。
- `candidate-applied / shared-texture-updated` 表示该共享纹理的像素已更新；不表示包中所有页面成功。逐项检查失败、待到达页面和实际画面。
- `address-not-found` 表示对应 PXL 已完成解码，但没有发现清单指定的图片角色或页地址。检查地址、ID 精度与目标游戏版本。

制作和加密导出的公共解析、依赖检查与 Schema 均支持该目标。完整地址规则以 `PxlResourceAddress` 为准，旧的 `ResourceAddressDraft` API 继续保留；只有包内的 `loader: "pxl" + address` 会触发运行时替换。
