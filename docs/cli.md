# 命令行检查与加密工具

命令默认从仓库根目录执行。命令行程序需要 .NET 8 SDK；Python 与 `jsonschema` 用于 Schema 检查。

`inspect` 检查资源包及依赖，`capabilities` 输出编译能力，`encrypt` 导出密文资源。输入包含 Sensitive 内容时，使用包含该子目录的完整资源根目录。

## 检查依赖、能力与导出密文包

只检查资源根目录，执行 `inspect`；命令不生成目录或修改输入，明文、密文和混合文件都可读取：

```powershell
dotnet run --project AICResourceKit.ResourceEncryptor/AICResourceKit.ResourceEncryptor.csproj -c Debug -- inspect --input "D:/Assets/ReplaceTexture"
```

成功输出 JSON：`reportVersion: 1`、`evidenceKind: validated-pack-dependencies`、插件/契约版本、`manifests`、去重后的 `files` 与 `fileCount`。每个清单包含 ID、相对清单路径、Sensitive 标记；每个目标包含零起始索引、类型、运行时身份和依赖的 `kind/path/pageKey`。所有路径相对输入根目录，使用 `/`，不输出机器绝对目录。

检查与加密共用严格解析和依赖枚举：包含 `image`、`pages[].image`、`atlas`、`spine.json`，共享文件只导出一次，不包含未被引用的工程文件。资源包与依赖必须同属普通目录树或 `Sensitive/`。PNG 检查文件头，JSON 检查可解析，atlas 检查非空；游戏中的尺寸、布局和显示仍需运行验证。错误指出清单及可识别的目标索引、依赖文件。

查询当前工具编译对应的插件能力：

```powershell
dotnet run --project AICResourceKit.ResourceEncryptor/AICResourceKit.ResourceEncryptor.csproj -c Debug -- capabilities
```

其 `evidenceKind` 为 `compiled-capabilities`，`runtimeValidation` 为 `not-performed-by-this-report`。如果需要将纯 JSON 重定向到文件，先构建，再使用 `dotnet AICResourceKit.ResourceEncryptor/bin/Debug/net8.0/AICResourceKit.ResourceEncryptor.dll capabilities` 或 `inspect`，避免首次 `dotnet run` 构建消息混入输出。

需要发布加密包时执行：

```powershell
dotnet run --project AICResourceKit.ResourceEncryptor/AICResourceKit.ResourceEncryptor.csproj -c Debug -- encrypt --input "D:/Assets/ReplaceTexture" --output "D:/Assets/EncryptedReplaceTexture"
```

输入是完整资源根目录；有 Sensitive 内容时，它必须是该根目录下的子目录。输出目录必须尚不存在，父目录必须已存在，输入输出不能重叠。

三个命令成功均返回退出码 `0`；包、依赖或读写错误返回 `1`，参数错误返回 `2`。`encrypt` 会生成文件，仅需检查时使用 `inspect`；工具没有 `validate` 子命令。公共解析的 C# 示例见[工具集成](integration.md)。

输出保留原文件名和相对目录，现有 BEREENC v1 包仍兼容。加密后的清单不能直接交给[Schema 检查脚本](#用-schema-检查自己的清单)，应在加密前校验明文。

## 用 Schema 检查自己的清单

安装 `jsonschema` 后，以下 PowerShell 命令校验一份**明文**清单。先执行 `python -m pip install jsonschema`，再把 Python 代码中的 `manifest_path` 改为实际文件路径：

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

## 检查项目自带规则和测试向量

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
