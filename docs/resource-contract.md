# 资源目标与清单契约（P02）

状态：可测试草案，P10 交接前定版。现有可安装格式仍为 v2。PXL 子集已接入 v2 `loader: pxl`，普通 Spine 已接入 `type: spine-assets`；其他新地址草案仍只描述定位与校验规则。

资源包创建、校验命令及可运行的 C# API 示例见[资源契约使用说明](usage.md)。

## 机器规则与公共实现

| 内容 | 位置 |
| --- | --- |
| v2 作者 Schema（JSON Schema draft-07） | [resource-replacement.schema.json](resource-replacement/resource-replacement.schema.json) |
| 公共正反例、身份串与依赖结果 | [contract-vectors.json](resource-replacement/contract-vectors.json) |
| 新地址与多页映射样例 | [address-draft.examples.json](resource-replacement/address-draft.examples.json) |
| 无 Unity 依赖的公共语义 | [Contracts](../AICResourceKit/Contracts/ResourceManifest.cs) |
| P01 证据与未确认项 | [资源加载诊断](diagnostics.md) |

`AICResourceKit.Contracts` 的源文件由插件与加密工具共同编译。后续制作工具可以链接这些文件，或复用测试向量；不需要加载 Unity 才能解释清单字段。

- `ResourceManifest.Parse`：严格解析整个清单，返回包 ID、目标及显式依赖；无效目标或重复身份导致失败。
- `ResourceManifest.ReadHeader`、`IdentityOf`、`ReadTarget`：运行时分步解析，便于保留可识别的失败目标并隔离其他目标。
- `ResourceIdentity`：生成既有 v2 身份，提供 MTI 匹配谓词。
- `PxlResourceAddress.Parse`、`Describe`、`Embedded`、`Page`：PXL 可安装地址的解析、输出与构造。
- `SpineResourceAddress.Parse`、`Create`、`Describe`：普通查看器的加载范围、atlas 和 JSON 地址。
- `ResourceAddressDraft.IdentityOf`、`ValidatePages`：保留独立地址与完整页映射草案 API，不直接触发替换。

解析 API 接受已解码的 `Dictionary<string, object>`，数组为 `List<object>`，数字为 `float`、`double`、`int` 或 `long`。JSON 解码由宿主负责：插件沿用 Spine 解码器，加密工具使用 System.Text.Json。制作工具应保留字符串键的大小写，不将 PXL 的第二图片 ID 经单精度浮点数转换。

Schema 用于生成规范的小写清单；为兼容旧包，实际解析仍接受大小写混合的 `type`、`loader`。Schema 不判断运行时支持、依赖文件存在性、根目录越界、链接、PNG/atlas/骨架内容和组合后的有效性，也不能表达按部分字段生成身份后的去重。这些规则必须继续由公共语义与文件校验执行。向量的 `valid` 表示严格公共解析结果，`schemaValid` 表示作者 Schema 结果，两者有意分开。

## 三种不同标识

| 标识 | 用途 | 是否用于运行时匹配 |
| --- | --- | --- |
| 来源对象 ID | P01 目录中的对象 ID、序列化文件与字符串形式的 pathId，用于追溯调查输入 | 否 |
| 运行时身份 | 游戏加载参数的有范围组合 | 是 |
| 包 ID / 样例包 ID | 清单 `id`、配置启用及排序；样例名称仅用于测试 | 不参与目标身份 |

运行时身份不根据 `Texture2D.name`、Unity 临时 instance ID、绝对游戏路径、导出图片文件名或 wardrobe 任务 ID 推导。清单中这些说明性元数据不改变匹配。替换文件的相对路径定位的是候选内容，也不是游戏对象身份。

## v2 身份与匹配

身份串中的 `\n` 表示实际换行。字段使用序数、区分大小写比较，资源键不修剪、不改成小写。

| 类型 | 必需定位字段 | 身份串 |
| --- | --- | --- |
| 主立绘 Spine | `type=spine`、`key`、`jsonKey` | `spine\n{key}\n{jsonKey}` |
| MTI 图片 | `type=texture`、`loader=mti`、`assetKey`，可选 `imageKey` | `texture\nmti\n{assetKey}\n{imageKey 或空串}` |
| Resources 图片 | `type=texture`、`loader=resources`、`path`、`objectType` | `texture\nresources\n{path}\n{objectType}` |

Resources 的 `objectType` 仅接受 `Texture2D` 与 `Sprite`，两者是不同身份。同名图片位于不同 MTI 容器时也是不同目标。

`stand_battle.old` 必须使用 `key=stand_battle`、`jsonKey=stand_battle.old`。它与 `jsonKey=stand_battle` 独立，不根据文件名统一替换成同一个 key。

MTI 保留历史行为：

| 清单 `imageKey` | 匹配行为 |
| --- | --- |
| 省略或 `null` | 指定容器中的旧版通配匹配，包括实际 null、空串及具名键 |
| `""` | 只匹配实际空字符串，不匹配 null |
| 非空字符串 | 精确匹配原始图片键 |

省略/null 与显式空字符串的**身份串相同、匹配谓词不同**。因此不能在一个包内把它们当成两个不同目标。为保持兼容，P02 不重新编码旧身份。

PXL 主纹理的 `MTI.LoadContainerOneImage` 调用省略 `image_key`，使用省略/null 的清单形式；不要填写 `load_key`、角色显示名或导出 PNG 文件名。直接 `MTI.LoadImage` 使用容器与真实图片键表达 v2 目标，已接入 MImage 及缓存材质的更新；MTIOneImage 保留已有入口。Sprite 支持范围和首载要求见[使用说明](usage.md)。

## PXL 地址扩展

v2 新增 `type=texture`、`loader=pxl`、`address` 和 `image`。`address` 接受下文的 `pxl-image` 或 `pxl-page`，来源限定为实际跟踪到的 MTI 文本/字节读取。旧版插件拒绝该加载器，需更新后重启；旧包无需改写。

运行时身份为 `texture\npxl\n` 加 draft1 地址去掉 `draft1|` 后的长度前缀编码。它与 MTI 主纹理及独立 draft1 身份互相隔离。`imageType` 额外限定为 0、1、2。同一纹理的外部地址和打包地址是别名；同包重复覆盖同一物理纹理在运行时拒绝，不同包仍按列表顺序选择最后一项。

每个目标声明一个 PNG 依赖，可以在同一包内列出多页；不增加 Spine 多页 atlas 支持。完整字段示例与诊断复制方法见 [PXL 说明](pxl-replacement.md)。

## 普通 Spine 地址与多页扩展

v2 新增 `type=spine-assets`，用 `address={kind:spine-assets,loader,assetKey?,atlasKey,jsonKey}` 定位普通查看器。MTI 的 atlasKey 保留实际 `.atlas` 后缀；Resources 使用完整资源路径且不得提供 assetKey。身份为 `spine-assets\n` 加 draft1 地址去掉前缀后的长度编码，与主立绘身份分离。不能同时填写主立绘的顶层 key/jsonKey。

可使用单页 `image` 或完整 `pages`，二者互斥；每个映射包含 `pageKey` 和 `image`，精确匹配最终 atlas 页名。最后一个声明图片的目标层提供整套映射，缺页不从其他层补齐。所有页面 PNG 都是显式依赖；不提供候选图片时按原页名复用原纹理。只有骨架目标 `spine-assets` 支持该映射，主立绘 `spine` 仍使用单页。

共享 JSON 分段与兼容映射规则，显示参数仅支持 `skeletonScale`；不接受 `effects` 和其他主立绘显示字段。完整示例和生命周期见[普通 SpineViewer 说明](spine-viewer-replacement.md)。

## v2 字段、依赖与优先级

顶层为 `formatVersion=2`、非空 `id` 和非空 `targets`。Texture 目标必须提供 `image`。Spine 可提供 `image`、`atlas`、`spine.json + spine.replace`、兼容映射、显示参数和污渍策略，详细字段见 [现有资源包说明](resource-packs.md)。

`spine.replace` 允许 `bones`、`slots`、`constraints`、`skins`、`attachments`、`events`、`animations`、`all`。`all` 不能与其他段混用，`skins` 与 `attachments` 不能在同一目标层同时出现。空 Spine 目标以及只有 fallback、没有实际替换内容的目标被拒绝。

依赖枚举返回清单明确声明的 `image`、`pages[].image`、`atlas`、`spine.json`。不从 atlas 页名猜测候选 PNG。主立绘仍拒绝多页；普通查看器使用完整页映射。缺少已声明文件会使目标失败，不能把缺页当成透明页或套用第一张图片。

文件路径相对于当前清单。可以用 `../shared/page.png` 在同一资源根目录、同一授权树内共享文件；跨包共享本身不构成错误。解析后必须位于 `ReplaceTexture` 根下，不允许绝对路径、目录越界或 reparse point。普通目录与 `Sensitive` 不能跨界共享依赖。加密导出按规范化后的物理路径去重，保留相对目录布局。

同一包内重复身份拒绝整个包，即使重复目标中的一项已经有文件错误。两个成功解析且获授权的包具有相同 `id` 时，目录发现移除全部同 ID 包并报告错误。敏感包未获授权时不参与这次包冲突判断，但其声明 ID 仍用于保留配置行。

不同包的同一目标允许重叠。包按配置列表顺序应用，靠后的层优先；普通纹理取最后一个匹配目标，Spine 按现有分段与显示合成规则处理。同一包内 MTI 通配与具名目标的身份不同，允许重叠，其匹配次序仍按清单目标顺序，后匹配者优先。临时立绘预览的优先级不会改写配置顺序。

## 错误与版本

| 情况 | 运行时目录发现 | 作者加密工具 |
| --- | --- | --- |
| 不支持的版本、缺包头、空 targets、重复身份 | 拒绝整个包，保留可读取的声明 ID | 拒绝整个导出 |
| 未知类型/加载器、非对象目标 | 记录无法识别身份的目标错误，其余有效目标仍可登记 | 拒绝整个导出 |
| 已知身份但字段或依赖失败 | 记录无效身份，其余有效目标仍可登记 | 拒绝整个导出 |
| 依赖缺失、内容不兼容 | 报错，不把候选标为已应用 | 文件校验失败时不发布输出 |

登记有效目标不等于已经显示。PXL 对失败纹理恢复原像素，并按图片/页报告失败；其他独立纹理继续处理。刷新后有损坏候选时，现有运行时只在来源仍有授权、文件与路径约束仍成立的情况下尝试保留旧资源；撤销开关或 Sensitive 授权则恢复/释放。该行为仍由现有生命周期实现负责，P02 不扩大其保证。

旧目标的未知附加字段沿用旧版忽略行为；给主立绘 `spine` 添加 `pages` 不会启用多页。新增普通查看器类型明确解析 `pages`，拒绝不适用的显示和效果字段。当前公共解析及插件明确拒绝 `formatVersion=3`。后续若多页或新的加载范围确实无法兼容 v2，应在接入阶段设计并显式支持新版本，不批量改写已有包。

## 新地址草案 draft1

此草案不是 v3，也不是 `.replacement.json`。`draftVersion=1` 与包格式版本独立。样例中的原始图片 ID、页号若标为 synthetic，只用于验证规则，不能当作真实资源清单安装。

| kind | 身份组成 | 接入与证据边界 |
| --- | --- | --- |
| `spine-assets` | `loader` + MTI `assetKey`（仅 mti）+ `atlasKey` + `jsonKey` | 来自 prepareAtlasAssetsS 的真实参数；Resources 不允许混入 assetKey；已通过 v2 `type: spine-assets` 接入 |
| `atlas-region` | `loader` + MTI `assetKey`（仅 mti）+ `atlasKey` + `region` | 区域属于共享 atlas，不添加无关骨架 JSON；P06 接入 |
| `pxl-image` | `source={loader:mti,assetKey,textKey}` + `imageId` + `imageId2` + `role=I/P` | 来源需在读取 PXL 文本资产处绑定到角色对象，不能用 external_png_header 的默认值猜测；已通过 `loader: pxl` 接入 |
| `pxl-page` | 同一 PXL source + `storage=external,pageIndex`，或 `storage=packed,pageOrdinal,imageType` | 外部数组槽位与打包页序号不是同一标识；已通过 `loader: pxl` 接入 |
| `video` | `assetKey` + `clipKey` | 已观察到的 MTI VideoClip 来源；P08 处理播放器与生命周期 |

以上地址拒绝未声明字段和绝对路径。MPCC 当前只有内容 name/chr_name，尚未证明文件来源到实例的绑定；草案明确拒绝 `kind=mpcc`，P07 调查后再定义。来源不清楚的 Sprite 或 atlas 也不能仅从导出名称生成地址。

PXL 的 `imageId` 是 UInt32 十进制字符串；`imageId2` 是原始 double 的可往返十进制字符串，不能存成经过单精度解码的 JSON 数字。身份内部将其转换为 binary64 的 16 位大写十六进制位模式，正负零合并；非有限值拒绝。role 保留游戏 I/P 字段的真实含义，不预先把 P 宣称为所有资源的 mask。

草案身份使用 `draft1|` 加各组成字段的 UTF-8 字节数、冒号和原值串联。字段次序以表及公共实现为准，避免将路径分隔符作为字段分隔符而产生碰撞。旧 v2 身份保持原格式。

## 完整页映射规则

页映射为 `pageKey → image`。调用者必须先取得完整的目标页键集合；普通 Spine 使用叠加后的最终 atlas：Spine 使用真实 atlas 页名；PXL 使用已验证的页地址，不能使用导出文件名。`ValidatePages` 校验映射与原始集合严格一一对应：

- 缺页、未知页、重复页、空原始目录都拒绝；区分大小写，不按列表顺序猜测。
- 不同页可以明确指向同一个候选图片；不代表可以省略其中一页。
- 返回候选图片依赖，随后仍须检查文件存在性、根目录边界、Sensitive 授权及图片/UV 兼容性。
- 整页替换与逐区域合成若作用于同一对象，后续适配器必须建立重叠检测。在接入规则形成前，不承诺两种粒度能同时组合。

## 验证

```powershell
dotnet test AICResourceKit.Test/AICResourceKit.Test.csproj -c Debug -m:1 -nr:false
python tools/validate-contract-schema.py
```

Schema 检查脚本需要开发环境中的 `jsonschema` 包；主插件和加密工具不依赖 Python。测试覆盖两端语义、旧密文、真实旧包字段、重复与重叠、未知类型、错误隔离、草案来源范围、binary64 ID 和完整页映射。游戏入口、长期持有消费者及画面效果不属于这些纯逻辑测试的验收结果。
