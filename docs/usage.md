# 使用说明

本文介绍插件安装与日常操作、v2 资源包制作、清单校验、公共 API 和地址草案。玩家可先阅读第 1 节和第 2.4 节；资源包作者与工具开发者按下表选择后续内容。

当前可安装的清单版本为 `formatVersion: 2`；PXL 图片与页目标使用 `loader: pxl`，详见[PXL 使用说明](pxl-replacement.md)。普通剧情 Spine 与多页 atlas 使用 `type: spine-assets`，详见[普通 SpineViewer 使用说明](spine-viewer-replacement.md)。独立图集和 PICT 图片使用 `type: atlas-region` / `atlas-page`，详见[图集替换说明](atlas-replacement.md)。独立地址草案文件不能直接作为资源包安装。

## 1. 安装与日常操作

| 目的 | 使用方式 |
| --- | --- |
| 制作或修改资源包 | 按第 2 节创建清单，按第 3 节校验，然后在游戏中启用 |
| 检查项目的契约测试是否正常 | 运行第 3.1 节的现有检查命令 |
| 为自己的工具解析目标、检查重复或收集依赖 | 链接 `Contracts` 源码，使用第 4 节的 C# 示例 |
| 制作剧情 Spine 或多页 atlas 包 | 使用[普通 SpineViewer 替换](spine-viewer-replacement.md)中的清单与页映射 |
| 查看 MPCC 预设与图片依赖 | 使用[MPCC 调查工具](mpcc-inspection.md)，导出后复制已确认的 PXL 地址 |
| 替换 PICT 图片或独立 atlas 页 | 使用[图集替换](atlas-replacement.md)中的区域和整页目标 |
| 研究未来的视频目标 | 使用第 5 节的地址草案 API；不会触发资源替换 |
| 查看字段和错误处理的完整定义 | 阅读[资源目标与清单契约](resource-contract.md) |

除明确标为游戏目录的路径外，下面的命令均从 AICResourceKit 仓库根目录执行。C# 示例与加密工具需要 .NET 8 SDK；Schema 检查需要 Python 和 `jsonschema`。游戏插件运行不需要 Python。

### 1.1 安装插件

1. 在游戏中安装 BepInEx 5 和 UnityModBase 前置。
2. 将 `AICResourceKit.dll` 放入游戏的 `BepInEx/plugins/AICResourceKit/`。
3. 启动游戏，在 UnityModBase 配置界面中选择“AIC资源替换 / AICResourceKit”。首次运行会创建资源、日志目录和配置文件。

配置文件为 `BepInEx/plugins/AICResourceKit/AICResourceKit.cfg`。总开关在启动阶段决定是否初始化插件；如果启动时关闭了它，修改后需要重启游戏。已有 BetterExperience 资源功能的安装请先阅读[迁移说明](migration.md)，避免同时加载两套旧资源钩子。

更新插件时先退出游戏，再将本次构建的 DLL 部署到安装位置。Debug 与 Release 使用独立输出目录；构建 Debug 不会更新 `bin/Release/`。如果安装位置使用符号链接，应检查其实际指向，确保游戏加载的是包含所需功能的新 DLL。替换 DLL 后需要重启游戏；`Ctrl+T` 只刷新资源文件，不能加载新插件代码。

### 1.2 常用配置与文件位置

| 配置项 | 默认值 | 用途 |
| --- | --- | --- |
| `General.EnableMod` | `true` | 插件总开关，在启动游戏前设置 |
| `Texture.EnableResourceReplacement` | `false` | 启用资源替换 |
| `Texture.EnabledReplacementPacks` | 空列表 | 按包 ID 启用和排序，新发现的包默认关闭 |
| `Texture.EnableSensitivities` | `true` | 允许 `ReplaceTexture/Sensitive/` 下的资源包 |
| `Texture.EnableResourceDiagnostics` | `false` | 记录加载诊断；排查结束后关闭 |
| `Texture.ResourceDiagnosticFilter` | 空字符串 | 按目标筛选诊断，多条件使用分号分隔 |
| `Hotkey.FlushTextureHotkey` | `Ctrl+T` | 重新扫描资源文件 |

启用、停用资源包和调整排序会自动应用；编辑、新增或删除文件后使用刷新热键。清单仍存在但暂时解析失败时，配置行会保留，具体错误见日志。

| 内容 | 游戏目录中的位置 |
| --- | --- |
| 普通资源包 | `BepInEx/plugins/AICResourceKit/ReplaceTexture/` |
| 敏感资源包 | `BepInEx/plugins/AICResourceKit/ReplaceTexture/Sensitive/` |
| 插件日志 | `BepInEx/plugins/AICResourceKit/logs/` |
| 诊断报告 | `BepInEx/plugins/AICResourceKit/logs/resource-diagnostics.json` |

### 1.3 立绘控制与资源预览

在主界面角色立绘可用时，进入 UnityModBase 的 AICResourceKit 控制界面，打开“立绘”页：

1. 按中文名称或原始标识筛选并选择一个姿态。
2. 选择状态预设，按需调整“主状态”和“附加状态”。附加状态只控制外观，不改变角色实际异常状态。
3. 使用“应用一次”查看效果；需要持续观察时开启“锁定立绘”，关闭锁定后恢复游戏控制。

改选姿态会选择该姿态的首个预设；锁定期间的调整即时生效。不受姿态支持的状态标记会在应用时调整。控制状态只在当前会话内生效，不写入游戏存档。

新启用的主立绘包若能找到适用姿态，会在资源准备完成后短暂预览约 2 秒，再恢复此前的姿态、锁定状态和正常包顺序。临时预览不会修改包列表的正式顺序；没有可预览的主界面姿态时，应到目标场景中检查实际资源。

### 1.4 导出 MPCC 调色预设报告

在 AICResourceKit **控制界面 → 资源调查**中触发“导出 MPCC 报告”，读取 `BepInEx/plugins/AICResourceKit/logs/mpcc-inspection.json`。工具通过游戏原生读取器列出附带 MPCC 文件的角色键、部件、调色操作和已加载 PXL 页的地址，不应用预设、不改启用配置。无需开启持续诊断。

尚未加载的角色不会凭名称生成图片地址；正常进入相关场景后可再次导出。五个文件的调查结论、报告字段和可复用的 PXL 示例见[MPCC 调查说明](mpcc-inspection.md)。

## 2. 创建并启用一个 v2 资源包

### 2.1 准备目录和图片

以仅替换主立绘图片为例，在游戏目录中创建：

```text
BepInEx/plugins/AICResourceKit/ReplaceTexture/
└─ MyBattlePortrait/
   ├─ battle.replacement.json
   └─ battle.png
```

`battle.png` 应与对应原始单页 atlas 的画布尺寸和区域布局一致，使用 straight-alpha PNG。此示例没有更换 atlas 或骨架；任意尺寸、任意布局的图片不能直接套用。

### 2.2 编写清单

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

### 2.3 确认 MTI 的空图片键

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

### 2.4 安装、启用和排序

1. 将校验后的清单和依赖放入 `BepInEx/plugins/AICResourceKit/ReplaceTexture/`，保持相对路径。
2. 在 UnityModBase 配置界面选择 AICResourceKit，确认总开关 `EnableMod` 已在游戏启动前开启。
3. 开启 `EnableResourceReplacement`，在 `EnabledReplacementPacks` 中启用清单的 `id`。
4. 若资源放在 `ReplaceTexture/Sensitive/`，同时开启 `EnableSensitivities`。清单和依赖必须处于同一个普通目录树或 Sensitive 目录树中。
5. 新增或修改文件后按默认热键 `Ctrl+T` 重新扫描，然后触发对应姿态或资源加载。

列表越靠后的包优先。同一个包内不允许重复目标；不同包可以作用于同一目标。普通图片使用最后匹配的候选，Spine 使用既有分层合成规则。

资源未显示时，可以开启[资源加载诊断](diagnostics.md)，按目标键筛选，检查发现、加载和应用结果。Schema 通过或目标被发现都不等于画面已验证。

### 2.5 替换标题中的直接 MTI 图片

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

`MTIOneImage` 继续由已有单图入口处理，保留第 2.3 节的空键规则，避免它内部的 `LoadImage` 再次应用同一个目标。PXL 内嵌和额外页使用独立的 [PXL 适配器](pxl-replacement.md)。

`wplmode_` 虽存在于 `mti_title_wpl.dat`，其实际加载参数仍待确认，因此目前不提供该图片的可安装定位示例。ver030g 的两张标题图已验证首次加载与画面；修改资源后仍应检查刷新和关闭后的显示，自动测试不代替实机检查。

### 2.6 替换 Resources Sprite

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

`mgm_bun.pxls.bytes.texture_0` 与 `damage_backvoreenemy` 的 Sprite 消费入口尚未确认，不能直接把对象名填入 `path`。前者已确认的是同包 Texture2D 的 PXL 路径，不能据此判断 Sprite 也经过 Resources。详细证据与限制见[诊断说明](diagnostics.md)。

### 2.7 替换 PXL 图片与页面

使用 `type: "texture"`、`loader: "pxl"`、`address` 和 `image`。先从运行时诊断复制来源、原始 ID 或页序号，再准备同尺寸的完整 PNG；外部、内嵌、打包与额外页分别登记，保留原帧和图层关系。完整示例、共享页冲突及刷新规则见 [PXL 使用说明](pxl-replacement.md)。

### 2.8 替换剧情图集中的独立图片

使用 `type: "atlas-region"`、实际 atlas 来源地址及候选整页 PNG，只复制指定区域的像素。使用 `type: "atlas-page"` 则明确覆盖整页。两种入口均不需要骨架 JSON，不改原区域布局；同一页不允许重叠的整页和区域目标。三张 `noel_peeping__0000/0001/0002` 的完整示例、地址获取及刷新规则见[图集替换说明](atlas-replacement.md)。

## 3. 校验清单

### 3.1 检查项目自带规则和测试向量

开发环境尚未安装 `jsonschema` 时执行：

```powershell
python -m pip install jsonschema
```

检查 Schema 及自带的正反例：

```powershell
python tools/validate-contract-schema.py
```

成功时输出 `Schema and all N authoring vectors passed.`，其中 N 为当前向量数。这个脚本只读取仓库中的固定向量，没有接收自有清单路径的参数。

运行公共语义和兼容性测试：

```powershell
dotnet test AICResourceKit.Test/AICResourceKit.Test.csproj -c Debug -m:1 -nr:false
```

已有依赖缓存时可添加 `--no-restore`。测试项目引用 net472 插件时可能出现既有 NU1702 警告；以实际测试结果判断是否通过。

### 3.2 用 Schema 检查自己的清单

以下 PowerShell 命令校验一份**明文**清单。先把 Python 代码中的 `manifest_path` 改为实际文件路径：

```powershell
@'
import json
from pathlib import Path
from jsonschema import Draft7Validator

schema_path = Path("docs/resource-replacement/resource-replacement.schema.json")
manifest_path = Path("D:/Assets/ReplaceTexture/MyBattlePortrait/battle.replacement.json")

schema = json.loads(schema_path.read_text(encoding="utf-8-sig"))
manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
Draft7Validator.check_schema(schema)
errors = list(Draft7Validator(schema).iter_errors(manifest))
for error in errors:
    location = "/".join(str(part) for part in error.absolute_path) or "<root>"
    print(f"{location}: {error.message}")
if errors:
    raise SystemExit(1)
print("Schema passed:", manifest_path)
'@ | python -
```

作者 Schema 要求小写 `type`、`loader`。已有包使用大写或混合大小写时，公共解析保留兼容行为，因此可能出现“Schema 不通过，但旧包仍可被解析”的情况；新包应使用小写。

Schema 不读取图片，也不判断目标是否实际存在。下面三类验证需分别完成：

| 验证 | 主要检查 |
| --- | --- |
| Schema | 字段形状、类型、规范写法及部分组合约束 |
| 公共解析与依赖检查 | 身份重复、字段语义；进一步检查声明文件、路径和授权边界 |
| 游戏运行 | 加载入口、候选应用、消费者刷新和画面效果 |

### 3.3 可选：校验依赖并导出密文包

需要发布加密包时执行：

```powershell
dotnet run --project AICResourceKit.ResourceEncryptor/AICResourceKit.ResourceEncryptor.csproj -c Debug -- encrypt --input "D:/Assets/ReplaceTexture" --output "D:/Assets/EncryptedReplaceTexture"
```

输入是完整资源根目录；有 Sensitive 内容时，它必须是该根目录下的子目录。输出目录必须尚不存在，父目录必须已存在，输入输出不能重叠。

工具严格解析所有清单，检查声明依赖并生成密文，成功返回退出码 `0`。这条命令会生成文件，不是只读校验命令；当前工具没有独立的 `validate` 子命令。仅需检查字段语义时可使用下一节的示例。

输出保留原文件名和相对目录，现有 BEREENC v1 包仍兼容。加密成功不能代替游戏中的 atlas、骨架合成和显示检查。加密后的清单不能直接交给上述 JSON Schema 脚本，应在加密前校验明文。

## 4. 在制作工具中复用公共解析

### 4.1 链接公共源码

可以将 `AICResourceKit/Contracts/` 下全部 C# 文件链接到自己的工程。它们不引用 Unity，也不要求引用主插件 DLL。

例如，在仓库根目录下新建 `ContractExample/`，保存以下 `ContractExample.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net8.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>disable</Nullable>
    </PropertyGroup>
    <ItemGroup>
        <Compile Include="../AICResourceKit/Contracts/*.cs" Link="Contracts/%(Filename)%(Extension)" />
    </ItemGroup>
</Project>
```

若工程放在其他目录，调整 `Compile Include`。公共源码没有单独发布的 NuGet 包。不要同时链接源码和引用含相同契约类型的程序集，以免出现类型重名。

### 4.2 解析明文 JSON 并输出依赖

在 `ContractExample/Program.cs` 中保存以下完整示例：

```csharp
using AICResourceKit.Contracts;
using System.Text.Json;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: ContractExample <manifest.replacement.json>");
    return 2;
}

try
{
    using var document = JsonDocument.Parse(File.ReadAllText(args[0]));
    var tree = Decode(document.RootElement) as Dictionary<string, object>
        ?? throw new InvalidDataException("Expected JSON object.");
    var manifest = ResourceManifest.Parse(tree);
    Console.WriteLine("Package: " + manifest.Id);
    foreach (var target in manifest.Targets)
    {
        Console.WriteLine("Target: " + target.Identity.Replace("\n", " / "));
        foreach (var dependency in target.Dependencies)
            Console.WriteLine("  " + dependency.Kind + ": " + dependency.Path);
    }
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static object Decode(JsonElement value) => value.ValueKind switch
{
    JsonValueKind.Object => value.EnumerateObject().ToDictionary(
        property => property.Name, property => Decode(property.Value), StringComparer.Ordinal),
    JsonValueKind.Array => value.EnumerateArray().Select(Decode).ToList(),
    JsonValueKind.String => value.GetString(),
    JsonValueKind.Number => value.GetDouble(),
    JsonValueKind.True => true,
    JsonValueKind.False => false,
    JsonValueKind.Null => null,
    _ => throw new InvalidDataException("Unsupported JSON value.")
};
```

运行：

```powershell
dotnet run --project ContractExample/ContractExample.csproj -c Debug -- "D:/Assets/ReplaceTexture/MyBattlePortrait/battle.replacement.json"
```

对第 2.2 节清单，输出为：

```text
Package: my-battle-portrait
Target: spine / stand_battle / stand_battle
  image: battle.png
```

输出为了便于阅读将身份串中的换行显示为 ` / `；实际程序匹配仍应使用原始 `target.Identity`。

`JsonSerializer.Deserialize<Dictionary<string, object>>` 默认会留下 `JsonElement` 值，不能直接作为本 API 的对象树。示例的 `Decode` 将嵌套对象和数组转换为要求的字典与列表。

`ResourceManifest.Parse` 使用严格模式：一个目标无效或身份重复就抛出异常。插件需要逐目标错误隔离时，使用 `ReadHeader`、`IdentityOf`、`ReadTarget`；不能简单吞掉异常后将整个包标为有效。

`Dependencies` 只枚举清单明确声明的 `image`、`pages[].image`、`atlas` 和 `spine.json`，不会打开文件。这些相对路径尚未去重、检查存在性或确认授权，也不会自动从 atlas 页名补出图片。自有工具后续必须按契约完成文件检查；单纯调用 `Path.GetFullPath` 不能代替根目录及链接检查。

### 4.3 使用测试向量核对自己的实现

读取 [contract-vectors.json](resource-replacement/contract-vectors.json)，对每个 `cases` 项执行解析：

- `valid=true`：解析应成功，目标身份顺序与 `identities` 一致，依赖的 `kind:path` 序列与 `dependencies` 一致。
- `valid=false`：严格解析应失败，不能静默丢弃错误后当作成功。
- `schemaValid`：仅用于 Schema 结果，不能替代 `valid`。

身份键按大小写精确匹配。包 ID 与来源对象 ID 不参与目标身份；同名异容器、多 JSON、MTI 空键、未知类型和重复身份均有对应向量。

## 5. 验证新地址和多页映射草案

该 API 用于开发验证，不会创建替换包或向游戏注册适配器。不要把 `draft1|...` 身份串填进 v2 的 `key`，也不要将示例改名为 `.replacement.json` 安装。

下面的 PXL 来源、ID 和页名都是合成示例，只用于演示调用。可将第 4 节的 `Program.cs` 暂时替换为：

```csharp
using AICResourceKit.Contracts;

var address = new Dictionary<string, object>
{
    ["kind"] = "pxl-image",
    ["source"] = new Dictionary<string, object>
    {
        ["loader"] = "mti",
        ["assetKey"] = "Pxl/example.pxls",
        ["textKey"] = "example.pxls"
    },
    ["imageId"] = "123",
    ["imageId2"] = "0.5",
    ["role"] = "I"
};

Console.WriteLine(ResourceAddressDraft.IdentityOf(address));

var dependencies = ResourceAddressDraft.ValidatePages(
    new[] { "page-a.png", "page-b.png" },
    new[]
    {
        new ResourcePageDraft("page-a.png", "candidate/a.png"),
        new ResourcePageDraft("page-b.png", "candidate/b.png")
    });

foreach (var dependency in dependencies)
    Console.WriteLine(dependency.Kind + ": " + dependency.Path);
```

同样运行 `dotnet run --project ContractExample/ContractExample.csproj -c Debug`，这次无需传入清单路径。

实际使用必须先取得完整原始页目录，再调用 `ValidatePages`。缺页、未知页、重复页、大小写不匹配或空目录都会抛出 `InvalidDataException`。返回图片依赖不意味着图片文件已存在、尺寸合适或 UV 兼容。

PXL 的 `imageId`、`imageId2` 都以字符串传入；`imageId2` 应保留原始 double 精度。不得将 Unity instance ID、导出文件名或任务 ID 当成图片 ID。MPCC 来源绑定尚未确定，当前草案明确拒绝 `kind=mpcc`。

其他地址字段和示例见[地址草案样例](resource-replacement/address-draft.examples.json)与[契约说明](resource-contract.md)。

## 6. 常见问题

| 现象 | 检查方法 |
| --- | --- |
| `Duplicate target in package` | 检查同一包内的身份是否重复；特别检查省略/null 与空 `imageKey` 的组合 |
| `Unsupported replacement manifest version` | 使用 v2；不要将地址草案的版本号或未来格式写入 `formatVersion` |
| Schema 通过，但公共解析失败 | Schema 不能覆盖所有语义，例如按身份去重；查看解析异常和对应向量 |
| 公共解析通过，但游戏加载失败 | 检查依赖存在性、PNG/atlas/骨架内容、Sensitive 边界以及目标加载入口 |
| 提示 atlas 必须只有一页 | 主立绘 `type: spine` 只支持单页；普通查看器使用 `type: spine-assets` 和完整 `pages` 映射 |
| 文件存在但提示路径越界或授权树错误 | 按清单位置解析相对路径；共享文件必须在资源根目录内，且不能跨普通/Sensitive 边界 |
| Sprite 提示 packed、transformed layout 或尺寸不匹配 | 使用原整张纹理；当前不支持打包/旋转 Sprite，不能改用裁片尺寸绕过检查 |
| 已显示的 Resources 图片在首次启用包后未变化 | 在首次加载前启用，或重新加载对应界面；旧原始引用不会自动变成替换对象 |
| 工具能生成新地址，但游戏没有变化 | 独立地址草案不会触发替换；PXL 使用 v2 `loader: pxl`，普通 Spine 使用 `type: spine-assets`，其他类型等待对应适配器 |
| PXL 提示 address-not-found 或 page-awaiting-texture | 检查诊断中的真实来源、ID/页号，确认目标图片已由游戏加载 |
| 修改包后仍看到旧内容 | 按 `Ctrl+T` 刷新，检查包开关、排序和诊断中的失败记录；坏候选可能保留仍获授权的旧资源 |
