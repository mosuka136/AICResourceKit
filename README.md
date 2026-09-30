# AICResourceKit

Alice In Cradle 的资源替换插件与制作辅助工具，基于 BepInEx、Harmony 和 UnityModBase。

支持 v2 资源包、MTI 直接图片、单图容器、Unity Resources 与 PXL 图片/整页替换、主立绘 Spine 分段合成、剧情 Spine 多页与独立图集区域替换、包排序、敏感内容开关、刷新、立绘控制与临时预览。附带资源加载诊断、MPCC 调色预设与 PXL 依赖调查、清单契约和命令行加密工具。

## 快速使用

1. 安装 BepInEx 5 和 UnityModBase，将 `AICResourceKit.dll` 放入游戏的 `BepInEx/plugins/AICResourceKit/`。
2. 启动游戏，在 UnityModBase 配置界面选择 AICResourceKit。总开关 `EnableMod` 需要在启动时开启；启动时关闭过它，应开启后重启游戏。
3. 将资源包放入 `BepInEx/plugins/AICResourceKit/ReplaceTexture/`，开启“启用资源替换”，在资源包列表中启用需要的包。列表越靠后的包优先。
4. 修改资源文件后按默认热键 `Ctrl+T` 重新扫描。

配置文件为 `BepInEx/plugins/AICResourceKit/AICResourceKit.cfg`。已有 BetterExperience 资源包可按[迁移说明](docs/migration.md)转入。

## 正式文档

| 任务 | 文档 |
| --- | --- |
| 安装、配置、制作最小资源包、校验和使用公共 API | [使用说明](docs/usage.md) |
| Spine 分段替换、兼容映射、显示参数与完整包格式 | [资源包参考](docs/resource-packs.md) |
| PXL 内嵌图片、外部页与打包页替换 | [PXL 使用说明](docs/pxl-replacement.md) |
| 替换剧情 PICT 图片或独立图集页 | [图集替换说明](docs/atlas-replacement.md) |
| 调查 MPCC 调色文件及其 PXL 图片依赖 | [MPCC 调查说明](docs/mpcc-inspection.md) |
| 确认实际加载入口、读取诊断报告 | [资源加载诊断](docs/diagnostics.md) |
| 查字段语义、身份匹配、Schema 和地址草案 | [资源契约](docs/resource-contract.md) |
| 准备引用、构建、修改代码和维护文档 | [开发说明](docs/development.md) |
| 查看全部文档与适用范围 | [文档目录](docs/README.md) |

当前可安装格式为 `formatVersion: 2`，Spine 使用 straight-alpha PNG 和 Spine 4.1 JSON。主立绘 `type: spine` 使用单页 atlas；[普通剧情 Spine](docs/spine-viewer-replacement.md) 使用 `type: spine-assets`，支持显式多页映射。PXL 的 `loader: pxl` 支持图片和逐页替换。独立图集与 PICT 使用 `atlas-region` / `atlas-page`，无需骨架 JSON。各项能力见[诊断说明](docs/diagnostics.md)，后续设计见[资源工具计划](docs/planning/noel-resource-tools.md)。

## 构建与测试

先按[开发说明](docs/development.md)填充仓库根目录的 `ReferenceLibrary/`，然后执行：

```powershell
dotnet build AICResourceKit/AICResourceKit.csproj -c Debug -m:1 -nr:false
dotnet test AICResourceKit.Test/AICResourceKit.Test.csproj -c Debug -m:1 -nr:false
```

主插件目标为 .NET Framework 4.7.2，测试和加密工具为 .NET 8。主插件产物位于 `AICResourceKit/bin/Debug/AICResourceKit.dll`。测试不代替 Unity 游戏中的实际加载和画面检查。

## 许可证

资源功能源自 BetterExperience，沿用 [LGPL-3.0](LICENSE.txt)。
