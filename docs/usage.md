# 安装与使用

本文说明插件安装、配置、资源包制作和日常操作。字段定义见[资源契约](resource-contract.md)，发布前检查见[命令行工具](cli.md)，C# API 示例见[工具集成](integration.md)。

可安装清单使用 `formatVersion: 2`。图片、PXL、Spine 和图集各有不同的定位方式与限制，见[兼容性与支持范围](compatibility.md)。

## 安装与日常操作

### 安装插件

1. 在游戏中安装 BepInEx 5 和 UnityModBase 前置。
2. 将 `AICResourceKit.dll` 放入游戏的 `BepInEx/plugins/AICResourceKit/`。
3. 启动游戏，在 UnityModBase 配置界面中选择“AIC资源替换 / AICResourceKit”。首次运行会创建资源、日志目录和配置文件。

配置文件为 `BepInEx/plugins/AICResourceKit/AICResourceKit.cfg`。总开关在启动阶段决定是否初始化插件；如果启动时关闭了它，修改后需要重启游戏。旧版 BetterExperience 含资源替换功能时，应先更新至已移除该功能的版本，避免同时加载两套资源钩子。

更新插件时先退出游戏，再将本次构建的 DLL 部署到安装位置。Debug 与 Release 使用独立输出目录；构建 Debug 不会更新 `bin/Release/`。如果安装位置使用符号链接，应检查其实际指向，确保游戏加载的是包含所需功能的新 DLL。替换 DLL 后需要重启游戏；`Ctrl+T` 只刷新资源文件，不能加载新插件代码。

### 常用配置与文件位置

| 配置项 | 默认值 | 用途 |
| --- | --- | --- |
| `General.EnableMod` | `true` | 插件总开关，在启动游戏前设置 |
| `Texture.EnableResourceReplacement` | `false` | 启用资源替换 |
| `Texture.EnabledReplacementPacks` | 空列表 | 按包 ID 启用和排序，新发现的包默认关闭 |
| `Texture.EnableSensitivities` | `true` | 允许 `ReplaceTexture/Sensitive/` 下的资源包 |
| `Texture.EnableResourceDiagnostics` | `false` | 记录加载诊断；排查结束后关闭 |
| `Texture.ResourceDiagnosticFilter` | 空字符串 | 按目标筛选诊断，多条件使用分号分隔 |
| `Hotkey.FlushTextureHotkey` | `Ctrl+T` | 重新扫描资源文件 |

启用、停用资源包和调整排序会自动应用；编辑、新增或删除文件后使用刷新热键。停用包、关闭替换或撤销敏感授权在下一次主线程配置检查时恢复被撤销的资源；启用和排序连续调整合并约 0.15 秒后生效。清单仍存在但暂时解析失败时，配置行会保留，可在“控制界面 → 资源调查”查看错误。

| 内容 | 游戏目录中的位置 |
| --- | --- |
| 普通资源包 | `BepInEx/plugins/AICResourceKit/ReplaceTexture/` |
| 敏感资源包 | `BepInEx/plugins/AICResourceKit/ReplaceTexture/Sensitive/` |
| 插件日志 | `BepInEx/plugins/AICResourceKit/logs/` |
| 当前状态报告 | `BepInEx/plugins/AICResourceKit/logs/resource-status.json` |
| 诊断报告 | `BepInEx/plugins/AICResourceKit/logs/resource-diagnostics.json` |

### 立绘控制与资源预览

在主界面角色立绘可用时，进入 UnityModBase 的 AICResourceKit 控制界面，打开“立绘”页：

1. 按中文名称或原始标识筛选并选择一个姿态。
2. 选择状态预设，按需调整“主状态”和“附加状态”。附加状态只控制外观，不改变角色实际异常状态。
3. 使用“应用一次”查看效果；需要持续观察时开启“锁定立绘”，关闭锁定后恢复游戏控制。

改选姿态会选择该姿态的首个预设；锁定期间的调整即时生效。不受姿态支持的状态标记会在应用时调整。控制状态只在当前会话内生效，不写入游戏存档。

新启用的主立绘包若能找到适用姿态，会在资源准备完成后短暂预览约 2 秒，再恢复此前的姿态、锁定状态和正常包顺序。临时预览不会修改包列表的正式顺序；没有可预览的主界面姿态时，应到目标场景中检查实际资源。

### 导出 MPCC 调色预设报告

在 AICResourceKit **控制界面 → 资源调查**中触发“导出 MPCC 报告”，读取 `BepInEx/plugins/AICResourceKit/logs/mpcc-inspection.json`。工具通过游戏原生读取器列出附带 MPCC 文件的角色键、部件、调色操作和已加载 PXL 页的地址，不应用预设、不改启用配置。无需开启持续诊断。

尚未加载的角色不会凭名称生成图片地址；正常进入相关场景后可再次导出。五个文件的调查结论、报告字段和可复用的 PXL 示例见[MPCC 调查说明](mpcc-inspection.md)。

### 查看加载结果与刷新

进入 AICResourceKit **控制界面 → 资源调查**。加载结果每秒更新；“状态筛选”可输入包 ID、目标键或错误文本，只筛选显示，不改变资源包开关。“刷新资源”与配置的刷新热键调用同一入口；默认热键为 `Ctrl+T`。操作和报告字段见[资源状态说明](resource-status.md)。

修改 PNG、atlas、JSON 或清单后刷新，等待“准备中”结束，再到对应场景查看。“未加载”表示尚未观察到该目标的消费者，不能据此判断路径正确或错误。普通图片、PXL、图集和剧情 Spine 不触发主立绘姿态预览。

需要保存当前结果时触发“导出资源状态”。报告包含全部目标及包/文件错误，不受状态筛选影响，也不要求开启持续诊断。先看状态报告定位具体包与目标；需要追踪加载入口和操作历史时，再使用[资源加载诊断](diagnostics.md)。

## 创建并启用一个 v2 资源包

### 准备目录和图片

以仅替换主立绘图片为例，在游戏目录中创建：

```text
BepInEx/plugins/AICResourceKit/ReplaceTexture/
└─ MyBattlePortrait/
   ├─ battle.replacement.json
   └─ battle.png
```

`battle.png` 应与对应原始单页 atlas 的画布尺寸和区域布局一致，使用 straight-alpha PNG。此示例没有更换 atlas 或骨架；任意尺寸、任意布局的图片不能直接套用。

### 编写清单

将以下内容保存为 `battle.replacement.json`，使用 UTF-8 编码：

```json
{
    "formatVersion": 2,
    "id": "my-battle-portrait",
    "targets": [
        {
            "type": "spine",
            "key": "stand_battle",
            "jsonKey": "stand_battle",
            "image": "battle.png"
        }
    ]
}
```

`id` 是资源包在配置列表中的标识，建议保持稳定；同一资源根目录内的不同清单不能重复使用同一个包 ID。`key` 和 `jsonKey` 来自游戏的实际加载参数。`image` 是相对于当前清单的候选文件路径，可以自行命名；改变图片文件名不会改变目标身份。

如果实际目标是历史 JSON `stand_battle.old`，保留 `key: "stand_battle"`，只将 `jsonKey` 改为 `"stand_battle.old"`。一个包可以同时声明这两个目标，分别配置图片；不要将二者写成重复目标。

完整的 Spine 分段替换、兼容映射和显示参数示例见[自定义立绘说明](resource-packs.md)。这里提供的是清单写法示例，图片制作与实际画面仍需单独检查。

### 确认 MTI 的空图片键

旧 PXL 外部主纹理入口的定位写法如下。该示例只表达已知主纹理地址，不代表额外页、内嵌图片或所有消费者已经支持替换。

```json
{
    "formatVersion": 2,
    "id": "my-noel-main-texture",
    "targets": [
        {
            "type": "texture",
            "loader": "mti",
            "assetKey": "PxlNoel/noel.pxls.bytes.texture_0",
            "image": "noel-main.png"
        }
    ]
}
```

这里省略 `imageKey` 是有意的，不要填入 `load_key`、角色显示名或导出的 PNG 文件名。

| 写法 | 现有匹配行为 |
| --- | --- |
| 省略 `imageKey` 或写成 `null` | 在指定容器内使用旧版通配匹配 |
| `"imageKey": ""` | 只匹配实际空字符串，不匹配 null |
| `"imageKey": "原始图片键"` | 精确匹配该键，区分大小写 |

省略/null 与显式空字符串生成的旧版身份串相同，不能在同一个包内各写一个来表示两个目标。

### 安装、启用和排序

1. 将校验后的清单和依赖放入 `BepInEx/plugins/AICResourceKit/ReplaceTexture/`，保持相对路径。
2. 在 UnityModBase 配置界面选择 AICResourceKit，确认总开关 `EnableMod` 已在游戏启动前开启。
3. 开启 `EnableResourceReplacement`，在 `EnabledReplacementPacks` 中启用清单的 `id`。
4. 若资源放在 `ReplaceTexture/Sensitive/`，同时开启 `EnableSensitivities`。清单和依赖必须处于同一个普通目录树或 Sensitive 目录树中。
5. 新增或修改文件后按默认热键 `Ctrl+T` 重新扫描，然后触发对应姿态或资源加载。

列表越靠后的包优先。同一个包内不允许重复目标；不同包可以作用于同一目标。普通图片使用最后匹配的候选，Spine 使用既有分层合成规则。

资源未显示时，可以开启[资源加载诊断](diagnostics.md)，按目标键筛选，检查发现、加载和应用结果。Schema 通过或目标被发现都不等于画面已验证。

### 替换标题中的直接 MTI 图片

`MTI.LoadImage` 使用容器和真实图片键定位，清单仍为 v2。ver030g 源码已确认标题使用 `assetKey: "MTI_title"`，图片键为 `key_noel` 和 `difficulty`。

例如在 `ReplaceTexture/TitleImages/` 下放入清单和两张与原纹理尺寸相同的完整 PNG：

```json
{
    "formatVersion": 2,
    "id": "my-title-images",
    "targets": [
        {
            "type": "texture",
            "loader": "mti",
            "assetKey": "MTI_title",
            "imageKey": "key_noel",
            "image": "key-noel.png"
        },
        {
            "type": "texture",
            "loader": "mti",
            "assetKey": "MTI_title",
            "imageKey": "difficulty",
            "image": "difficulty.png"
        }
    ]
}
```

容器与图片键区分大小写；`assetKey` 保留游戏传入的大小写，不能根据磁盘上的 `mti_title.dat` 改写。只更换其中一张图时，移除不需要的目标即可。同名图片位于其他容器时不会命中这两个目标。

启用资源包后查看标题及难度选择界面。修改 PNG 后按 `Ctrl+T`，资源准备完成后更新已登记的 `MImage` 和它的缓存材质；后续刷新沿用替换纹理引用，关闭资源包恢复原纹理。直接入口首次返回前同步准备图片，大图首载可能有短暂等待。

需要排查时，将诊断筛选设为 `MTI_title`，查看 `entry-hit`、`candidate-applied` 和 `candidate-failed`。当前确认的消费者是标题的 `MImage` 材质缓存；自行复制纹理或材质的其他消费者仍需逐个核对。

`MTIOneImage` 继续由已有单图入口处理，保留[空图片键规则](#确认-mti-的空图片键)，避免它内部的 `LoadImage` 再次应用同一个目标。PXL 内嵌和额外页使用独立的 [PXL 适配器](pxl-replacement.md)。

`wplmode_` 虽存在于 `mti_title_wpl.dat`，其实际加载参数仍待确认，因此目前不提供该图片的可安装定位示例。实际支持范围及验证边界见[兼容性说明](compatibility.md)。

### 替换 Resources Sprite

下面仅是字段模板，`UI/ExampleSprite` 必须改成诊断中确认的实际 `Resources.Load` 路径：

```json
{
    "formatVersion": 2,
    "id": "my-resource-sprite",
    "targets": [
        {
            "type": "texture",
            "loader": "resources",
            "path": "UI/ExampleSprite",
            "objectType": "Sprite",
            "image": "sprite-texture.png"
        }
    ]
}
```

`sprite-texture.png` 必须覆盖原 Sprite 引用的**整张纹理**，尺寸与原纹理完全一致。不要将裁片导出图的尺寸当作源纹理尺寸，也不要移动原区域布局。

当前支持未打包 Sprite，包括矩形裁切和紧密网格；保留原逻辑 rect、pivot、border、pixelsPerUnit、顶点和三角形，并检查 UV 一致性。打包、旋转或需要其他 UV 变换的 Sprite 会报错并跳过，不会用矩形图强行替代。Spine atlas 与 Unity Sprite 的打包状态是不同概念。

应在资源首次加载前启用包。已经由插件返回的替换 Sprite/Texture 在刷新时保留引用；关闭后在同一纹理对象中恢复原图内容。若首次加载时未启用包，游戏可能仍持有原始对象，此时需重新加载对应界面或场景才能取得替换对象。

`mgm_bun.pxls.bytes.texture_0` 与 `damage_backvoreenemy` 的 Sprite 消费入口尚未确认，不能直接把对象名填入 `path`。前者已确认的是同包 Texture2D 的 PXL 路径，不能据此判断 Sprite 也经过 Resources。实际支持范围见[兼容性说明](compatibility.md)。

### 替换 PXL 图片与页面

使用 `type: "texture"`、`loader: "pxl"`、`address` 和 `image`。先从运行时诊断复制来源、原始 ID 或页序号，再准备同尺寸的完整 PNG；外部、内嵌、打包与额外页分别登记，保留原帧和图层关系。完整示例、共享页冲突及刷新规则见 [PXL 使用说明](pxl-replacement.md)。

### 替换剧情图集中的独立图片

使用 `type: "atlas-region"`、实际 atlas 来源地址及候选整页 PNG，只复制指定区域的像素。使用 `type: "atlas-page"` 则明确覆盖整页。两种入口均不需要骨架 JSON，不改原区域布局；同一页不允许重叠的整页和区域目标。三张 `noel_peeping__0000/0001/0002` 的完整示例、地址获取及刷新规则见[图集替换说明](atlas-replacement.md)。

## 检查与发布

制作完成后先使用 `inspect` 检查清单和依赖，再在游戏中核对目标地址、加载结果与画面。需要密文发布时使用 `encrypt`；加密保留文件名和相对路径。具体命令、退出码和 Schema 校验步骤见[命令行工具](cli.md)。

## 常见问题

| 现象 | 检查方法 |
| --- | --- |
| `Duplicate target in package` | 检查同一包内的身份是否重复；特别检查省略/null 与空 `imageKey` 的组合 |
| `Unsupported replacement manifest version` | 使用 v2；不要将独立地址的版本号或不支持的格式写入 `formatVersion` |
| Schema 通过，但公共解析失败 | Schema 不能覆盖所有语义，例如按身份去重；查看解析异常和对应向量 |
| 公共解析通过，但游戏加载失败 | 检查依赖存在性、PNG/atlas/骨架内容、Sensitive 边界以及目标加载入口 |
| 提示 atlas 必须只有一页 | 主立绘 `type: spine` 只支持单页；普通查看器使用 `type: spine-assets` 和完整 `pages` 映射 |
| 文件存在但提示路径越界或授权树错误 | 按清单位置解析相对路径；共享文件必须在资源根目录内，且不能跨普通/Sensitive 边界 |
| Sprite 提示 packed、transformed layout 或尺寸不匹配 | 使用原整张纹理；当前不支持打包/旋转 Sprite，不能改用裁片尺寸绕过检查 |
| 已显示的 Resources 图片在首次启用包后未变化 | 在首次加载前启用，或重新加载对应界面；旧原始引用不会自动变成替换对象 |
| 工具能生成新地址，但游戏没有变化 | 独立地址不会触发替换；按[资源契约](resource-contract.md)把受支持的目标写入 v2 清单 |
| PXL 提示 address-not-found 或 page-awaiting-texture | 检查诊断中的真实来源、ID/页号，确认目标图片已由游戏加载 |
| 修改包后仍看到旧内容 | 按 `Ctrl+T` 刷新，检查包开关、排序和诊断中的失败记录；坏候选可能保留仍获授权的旧资源 |
