# MPCC 调查与 PXL 图片依赖

MPCC 是游戏调色编辑器的预设文件，保存部件名、HSV 和色调曲线，不包含可直接替换的 PNG 或独立骨架。AICResourceKit 提供原生解码报告，并把已加载的图片依赖映射到现有 PXL 地址；资源包不新增 `type: mpcc`，也不直接覆盖原二进制文件。

## 导出报告

1. 更新插件 DLL 后重启游戏。
2. 在 UnityModBase 的 AICResourceKit **控制界面**打开“资源调查”页。
3. 触发“导出 MPCC 报告”。它是一次性操作，开关随后回到关闭状态，无需开启资源替换或持续诊断。
4. 读取 `BepInEx/plugins/AICResourceKit/logs/mpcc-inspection.json`。

工具只扫描当前游戏 `AliceInCradle_Data/StreamingAssets/mobpcc/` 中的 `.mpcc.bytes`，通过游戏自身的 `MobPCCContainer.readFromBytesFromFile` 解码。报告覆盖当前目录中的文件，重复导出覆盖上一份报告。它不应用调色、不加载其他角色、不改存档和包启用顺序。坏文件单独报告 `status: failed`，其他文件继续处理。

报告中的 `origin: explicit-inspection` 表示主动调查，`replacementApplied: false` 表示导出操作没有实施替换。不能把它当作剧情自然读取了这些文件的证据。

## 如何读取结果

| 字段 | 含义 |
| --- | --- |
| `file`、`sha256`、`byteLength` | 实际读取的文件身份、摘要和长度 |
| `formatVersion` | 原 MPCC 文件版本，目前报告工具仅接受版本 0；与资源包 v2 无关 |
| `name`、`characterKey` | 文件内部的预设名与角色模式，保留大小写，不从文件名推导 |
| `parts[].name` | 调色作用的部件名 |
| `parts[].operations` | 原生读取器返回的操作类型；HSV 附带 h/s/v/flags，曲线标记为 TONECURVE |
| `emptyPalette` | 是否为空预设，空预设不会被补成不存在的颜色或图片 |
| `dependencies[].source` | 已由 PXL 加载入口确认的 MTI 文本来源 |
| `partsMetadataAvailable`、`partsMaskLoaded` | 当前角色是否有部位数据、遮罩图片是否到达；图片来源确认不等于预设可应用 |
| `matchedParts`、`missingParts` | 当前 PXL 是否有对应部件；缺失不自动改名或归到其他角色 |
| `pages[].role` | `source-color` 为原色页，`parts-mask` 为部件遮罩页 |
| `pages[].address`、`loaded`、`width/height` | 可用于 PXL 目标的地址、图片是否到达及当前尺寸 |

`dependencies` **只包含当前已登记的角色**，不是自动加载列表。标题页可能为空；正常进入相关场景后再导出。`NOEL` 通过游戏的 `MTR.Anoel_pxls` 成员与实际 `PxlNoel` 来源匹配，可能关联多个动作文件；其他角色沿用编辑器的 `MapChars/<character>.pxls` 加载规则，并要求实际来源一致。无法确认来源的对象不会生成猜测地址。

PCC 的 `AddChr` 使用外部槽位 0 作为原色、槽位 1 作为部件遮罩；报告仅列出当前存在的这两个输入槽位。遮罩的颜色用于识别部件，不是可随意重绘的普通外观图。其他 PXL 内嵌或打包页仍按 [PXL 说明](pxl-replacement.md)调查，不能由此推断它们都参与了 PCC。

## ver030g 的五个文件

| 文件 | 内部角色键 | 部件数 | 调查结论 |
| --- | --- | ---: | --- |
| `noel__231022_013830.mpcc.bytes` | `NOEL` | 2 | Noel 模式，包含 `服白`、`hair`；文件名小写不改变内部模式 |
| `NOEL__231022_013830_2.mpcc.bytes` | `NOEL` | 0 | 空调色预设，不含图片或有效调色操作 |
| `NOEL__darknoel.mpcc.bytes` | `NOEL` | 9 | 同一 Noel 模式的调色组合，不能据名称认定为独立角色或骨架 |
| `sub_i__231022_162602.mpcc.bytes` | `sub_i` | 7 | 关联 `MapChars/sub_i.pxls`，当前文件缺少所需部位数据和遮罩；游戏代码将 `citycaster_sub_i` 用作 Ixia 目标 |
| `sub_i__mzh.mpcc.bytes` | `sub_i` | 8 | 同一 `sub_i` 的另一组预设，同样缺少所需部位数据和遮罩 |

**ver030g 的 `sub_i` 预设不能直接套用到当前附带的角色数据。** 实机确认该角色只有原色页，没有 `APartsInfo` 和外部槽位 1；原生解码器在绑定部件时会报 `No Parts Data in this Character`。报告仍能读取预设、列出缺失部件，并提供原色页替换地址；它不会补造遮罩或修复原游戏数据。部分 Noel 动作文件也没有部位数据，应逐条查看结果。

各部件和输入图片以当前游戏导出的报告为准。原文件、图片及完整反编译源码不随项目发布。

已找到的文件调用链为 `MobGEditorContainer.loadPccPrompt` → 文件选择器 → `NKT.readSpecificFileBinary` → `MobPCCContainer.readFromBytesFromFile`。默认选择目录是 `Application.persistentDataPath/mobpcc`，与游戏附带的 StreamingAssets 目录不同；未发现自动扫描这五个文件的常规游戏调用。

`SandPartsColorChanger` 的 `NOEL` 分支使用主角 PXL 角色集合，退出编辑模式时可把调色结果交给 `PrAnimator.ApplyPCCData` / `PrPoseContainer.ApplyPCCData`。通用其他角色分支读取 `MapChars`。MobGenerator 内嵌的调色表使用另一条读取路径，不等同于自动读取独立 MPCC 文件。

## 替换已确认的图片依赖

先正常加载目标角色，再复制报告中的 `pages[].address`。例如 `sub_i` 的原色页使用以下 v2 包；`candidate.png` 必须与报告中的整页尺寸一致：

```json
{
    "formatVersion": 2,
    "id": "my-sub-i-image",
    "targets": [
        {
            "type": "texture",
            "loader": "pxl",
            "address": {
                "kind": "pxl-page",
                "source": {
                    "loader": "mti",
                    "assetKey": "MapChars/sub_i.pxls",
                    "textKey": "sub_i.pxls"
                },
                "storage": "external",
                "pageIndex": 0
            },
            "image": "candidate.png"
        }
    ]
}
```

按[安装与启用步骤](usage.md#安装启用和排序)使用该包，修改 PNG 后按 `Ctrl+T`。它替换角色共用的原色页，对使用这张图的所有动作生效，不以 MPCC 文件名限定影响范围。停用后恢复原像素。不要同时启用同一物理纹理的旧 MTI 整图替换。

PCC 已经生成的 RenderTexture 是计算结果。刷新 PXL 输入不会自动重新运行调色或改写已经烘焙的结果；正在使用调色编辑器时，应在图片更新后重新应用预设或重新进入编辑流程。报告工具不接管编辑器状态，也不自动把调色应用到主角。

## 运行时诊断

开启资源诊断后：

- `mpcc-decoded / palette-read`：游戏调用了解码器，记录部件与角色键；该方法本身没有文件路径参数，不能单凭预设名确定文件。
- `mpcc-decoded / inspection-decoded`：报告工具主动调用读取器，`details.file` 为本次文件，不表示游戏自然命中。
- `mpcc-pxl / pcc-source-bound`：PCC 实际调用 `AddChr`，记录其 PXL 来源和页面地址。

持续诊断与手动报告用途不同；报告定义见[诊断说明](diagnostics.md)，支持边界见[兼容性说明](compatibility.md)。原 MPCC 数据替换、自动预设加载及已烘焙结果的自动重算均不在当前支持范围内。
