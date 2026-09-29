# AICResourceKit

Alice In Cradle 的资源替换插件与制作辅助工具，基于 BepInEx、Harmony 和 UnityModBase。主插件使用 .NET Framework 4.7.2，测试及加密工具使用 .NET 8。

## 功能

- 发现和校验 v2 `.replacement.json` 资源包，按配置列表顺序组合，支持 Sensitive 目录授权。
- 沿用已有 MTI 单图、Unity Resources 和主立绘 Spine 替换，支持 Spine 分段合成、兼容映射和显示参数。
- 资源刷新、异步准备、失败隔离、恢复与释放。
- 主界面立绘姿态选择、状态编辑、锁定与资源包短暂预览。
- 默认关闭的 P01 资源加载诊断及 `ver030g` 静态调查清单。
- 独立命令行加密工具，兼容原 BEREENC v1 密文。

调查清单列出的加载入口不代表已实现对应替换。直接 MTI、多页 PXL、普通 SpineViewer、MPCC 和视频等后续能力仍按计划逐项实现。

## 构建

先从安装了 BepInEx 与 UnityModBase 的游戏填充引用：

```powershell
./AICResourceKit/setup-references.ps1 -GameDir "D:/Games/AliceInCradle"
dotnet build AICResourceKit/AICResourceKit.csproj -c Debug -m:1 -nr:false
dotnet test AICResourceKit.Test/AICResourceKit.Test.csproj -c Debug -m:1 -nr:false
```

引用位于本项目 `ReferenceLibrary/`，不依赖 BetterExperience 工程。构建产物为 `AICResourceKit/bin/Debug/AICResourceKit.dll`。

测试工程引用 net472 插件时可能出现 NU1702 框架兼容性警告。单元测试覆盖可在 .NET 8 执行的逻辑与方法签名，Unity 画面及实际加载仍需游戏验证。

## 使用

1. 安装 BepInEx 5 与 UnityModBase 前置，将插件放入 `BepInEx/plugins/AICResourceKit/`。
2. 启动游戏，在 UnityModBase 配置界面选择 AICResourceKit。配置文件位于 `BepInEx/plugins/AICResourceKit/AICResourceKit.cfg`。
3. 将资源包放入 `BepInEx/plugins/AICResourceKit/ReplaceTexture/`，开启“启用资源替换”，按需选择和排序资源包。
4. 修改资源后按默认热键 `Ctrl+T` 刷新。

插件和 Harmony 标识为 `com.buele.aicresourcekit`。更新后的 BetterExperience 可同时使用，资源替换由 AICResourceKit 管理。

## 文档

- [从 BetterExperience 迁移](MIGRATION.md)
- [资源包格式与自定义立绘](CUSTOM_PORTRAIT_REPLACEMENT.md)
- [P01 资源加载调查与诊断](RESOURCE_LOADING_DIAGNOSTICS.md)
- [后续实现计划](NOEL_RESOURCE_TOOL_PLAN.md)
- [开发说明](AICResourceKit/README-DEV.md)

## 许可证

资源功能源自 BetterExperience，沿用 [LGPL-3.0](LICENSE.txt)。
