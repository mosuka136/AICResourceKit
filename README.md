# AICResourceKit

Alice In Cradle 的资源替换插件与制作辅助工具，基于 BepInEx、Harmony 和 UnityModBase。

支持 MTI、Unity Resources、PXL 图片与整页替换，主立绘 Spine 分段合成，普通剧情 Spine 多页和独立图集区域替换。资源包可排序、启停、刷新，并受敏感内容开关控制。附带立绘预览、资源状态与加载诊断、MPCC 依赖调查，以及命令行检查和加密工具。

## 快速使用

1. 安装 BepInEx 5 和 UnityModBase，将 `AICResourceKit.dll` 放入游戏的 `BepInEx/plugins/AICResourceKit/`。
2. 启动游戏，在 UnityModBase 配置界面选择 AICResourceKit。总开关 `EnableMod` 需要在启动时开启；启动时关闭过它，应开启后重启游戏。
3. 将资源包放入 `BepInEx/plugins/AICResourceKit/ReplaceTexture/`，开启“启用资源替换”，在列表中启用需要的包。列表越靠后的包优先。
4. 修改文件后按默认热键 `Ctrl+T`，或在“控制界面 → 资源调查”触发“刷新资源”。同页可查看加载结果、导出当前状态。

配置文件为 `BepInEx/plugins/AICResourceKit/AICResourceKit.cfg`。

## 文档

| 内容 | 入口 |
| --- | --- |
| 安装、配置、资源包制作与日常操作 | [使用说明](docs/usage.md) |
| 支持类型、游戏基线与已知限制 | [兼容性与支持范围](docs/compatibility.md) |
| 检查、能力查询与加密发布 | [命令行工具](docs/cli.md) |
| 字段、身份、依赖、Schema 与公共 API | [资源契约](docs/resource-contract.md)、[工具集成](docs/integration.md) |
| 构建、架构、测试与版本维护 | [开发说明](docs/development.md) |
| 专项替换指南与报告说明 | [文档目录](docs/README.md) |

可安装清单为 `formatVersion: 2`，兼容 BEREENC v1 密文。Spine 使用 straight-alpha PNG 和 Spine 4.1 JSON；主立绘支持单页，普通剧情 Spine 支持显式多页。视频替换不受支持。编译能力见[capabilities.json](docs/resource-replacement/capabilities.json)，具体游戏地址应以实际加载报告为准。

## 构建与测试

按[开发说明](docs/development.md)准备 `ReferenceLibrary/`，然后在仓库根目录执行：

```powershell
dotnet build AICResourceKit/AICResourceKit.csproj -c Debug -m:1 -nr:false
dotnet test AICResourceKit.Test/AICResourceKit.Test.csproj -c Debug -m:1 -nr:false
python tools/validate-contract-schema.py
```

主插件目标为 .NET Framework 4.7.2，测试和命令行工具为 .NET 8。插件产物位于 `AICResourceKit/bin/Debug/AICResourceKit.dll`；构建不会自动部署到游戏。

## 许可证

资源功能源自 BetterExperience，沿用 [LGPL-3.0](LICENSE.txt)。
