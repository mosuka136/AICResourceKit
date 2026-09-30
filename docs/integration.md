# 公共 API 与工具集成

插件和命令行工具链接同一组无 Unity 依赖的契约源码。外部制作工具可复用这些源码，或使用共同测试向量验证自己的解析器。文件读取和加密处理可调用[命令行工具](cli.md)，不必重复实现 BEREENC。

## 版本与能力

调用 `ResourceCapabilities.Describe()` 或 CLI `capabilities`，检查 `contractVersion`、`manifestVersions` 和所需 `features[].supported`。能力来源为编译版本，不读取游戏状态；实际 DLL 的相同能力表也包含在当前资源状态报告的 `capabilities` 字段中。

契约 1 定义当前可安装语义。清单 `formatVersion`、各报告 `reportVersion`、契约版本与插件版本用途不同，不要求数值一致。报告消费者应忽略未知附加字段；既有字段含义或结构不兼容时升级对应版本。

## 链接公共源码

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

## 解析明文 JSON 并输出依赖

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

对[最小主立绘清单](usage.md#编写清单)，输出为：

```text
Package: my-battle-portrait
Target: spine / stand_battle / stand_battle
  image: battle.png
```

输出为了便于阅读将身份串中的换行显示为 ` / `；实际程序匹配仍应使用原始 `target.Identity`。

`JsonSerializer.Deserialize<Dictionary<string, object>>` 默认会留下 `JsonElement` 值，不能直接作为本 API 的对象树。示例的 `Decode` 将嵌套对象和数组转换为要求的字典与列表。

`ResourceManifest.Parse` 使用严格模式：一个目标无效或身份重复就抛出异常。插件需要逐目标错误隔离时，使用 `ReadHeader`、`IdentityOf`、`ReadTarget`；不能简单吞掉异常后将整个包标为有效。

`Dependencies` 只枚举清单明确声明的 `image`、`pages[].image`、`atlas` 和 `spine.json`，不会打开文件。这些相对路径尚未去重、检查存在性或确认授权，也不会自动从 atlas 页名补出图片。自有工具后续必须按契约完成文件检查；单纯调用 `Path.GetFullPath` 不能代替根目录及链接检查。

## 使用测试向量核对自己的实现

读取 [contract-vectors.json](resource-replacement/contract-vectors.json)，对每个 `cases` 项执行解析：

- `valid=true`：解析应成功，目标身份顺序与 `identities` 一致，依赖的 `kind:path` 序列与 `dependencies` 一致。
- `valid=false`：严格解析应失败，不能静默丢弃错误后当作成功。
- `schemaValid`：仅用于 Schema 结果，不能替代 `valid`。

身份键按大小写精确匹配。包 ID 与来源对象 ID 不参与目标身份；同名异容器、多 JSON、MTI 空键、未知类型和重复身份均有对应向量。

## 运行时报告

| 数据 | 用途 |
| --- | --- |
| CLI `inspect` | 列出清单、身份和去重后的相对依赖路径；不验证真实游戏消费者 |
| [资源状态](resource-status.md) | 当前目标地址、声明文件、选中及实际应用包、消费者计数和错误 |
| [资源诊断](diagnostics.md) | 实际加载参数、入口事件与共享消费者证据 |
| [MPCC 报告](mpcc-inspection.md) | 显式读取的预设和已登记 PXL 输入映射 |

清单声明、文件检查和真实加载证据应分别处理。无法确认来源时保留未知状态，不能从目录名、显示名或单次未命中推导地址。具体基线和限制见[兼容性说明](compatibility.md)。

## 独立地址校验 API

`ResourceAddressDraft` 保留独立地址和完整页映射的校验 API，不会创建替换包或向游戏注册适配器。不要把 `draft1|...` 身份串填进 v2 的 `key`，也不要将示例改名为 `.replacement.json` 安装。

下面的 PXL 来源、ID 和页名都是合成示例，只用于演示调用。在独立的 `ContractExample/Program.cs` 中可使用：

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

PXL 的 `imageId`、`imageId2` 都以字符串传入；`imageId2` 应保留原始 double 精度。不得将 Unity instance ID、导出文件名或包 ID 当成图片 ID。MPCC 调查输出已加载的 PXL 来源，不定义独立的 MPCC 替换身份；该 API 拒绝 `kind=mpcc`。

可安装地址使用 `PxlResourceAddress`、`SpineResourceAddress` 和 `AtlasResourceAddress`。字段规则见[资源契约](resource-contract.md)。
