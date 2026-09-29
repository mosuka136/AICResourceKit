# 开发与维护说明

## 环境与引用

主插件使用 .NET Framework 4.7.2；加密工具和 xUnit 测试使用 .NET 8。需要能够构建这些目标框架的 .NET SDK，以及 .NET Framework 4.7.2 引用程序集。Python 和 `jsonschema` 仅用于作者 Schema 检查，不是游戏插件依赖。

在仓库根目录创建 `ReferenceLibrary/`，从自己安装的目标版本游戏、BepInEx 和 UnityModBase 中复制以下 DLL。目录内容不纳入 Git。不要混用不同游戏版本的程序集。

| 来源 | DLL |
| --- | --- |
| 游戏 `AliceInCradle_Data/Managed/` | `Assembly-CSharp.dll`、`unsafeAssem.dll`、`better.dll`、`pixelliner.dll`、`spine-unity.dll`、`Unity.InputSystem.dll` |
| 同上：Unity 模块 | `UnityEngine.dll`、`UnityEngine.CoreModule.dll`、`UnityEngine.IMGUIModule.dll`、`UnityEngine.TextRenderingModule.dll`、`UnityEngine.ImageConversionModule.dll` |
| 同上：测试额外引用 | `Better.UnsafeGeneric.dll`、`UnityEngine.Physics2DModule.dll`、`UnityEngine.VideoModule.dll` |
| `BepInEx/core/` | `0Harmony.dll`、`BepInEx.dll` |
| 安装的 UnityModBase | `UnityModBase.dll`、`UnityModBase.BepInExLauncher.dll` |

准确的引用与目标框架分别登记在[主插件工程](../AICResourceKit/AICResourceKit.csproj)、[测试工程](../AICResourceKit.Test/AICResourceKit.Test.csproj)和[加密工具工程](../AICResourceKit.ResourceEncryptor/AICResourceKit.ResourceEncryptor.csproj)中。项目不依赖 BetterExperience 工程。

## 常用命令

在仓库根目录执行：

```powershell
dotnet build AICResourceKit/AICResourceKit.csproj -c Debug -m:1 -nr:false
dotnet test AICResourceKit.Test/AICResourceKit.Test.csproj -c Debug -m:1 -nr:false
python tools/validate-contract-schema.py
```

已有依赖缓存时，dotnet 命令可加 `--no-restore`。测试引用 net472 插件时会出现既有 NU1702 框架兼容性警告，应同时检查构建错误及测试结果。

仅修改文档时检查链接、示例和文件格式即可，无需重复跑全部测试。修改清单或公共契约时，运行相关现有测试和 Schema 向量；涉及运行时拆分等跨模块重构时，运行完整现有测试。若还没有安装 `jsonschema`，执行 `python -m pip install jsonschema`。

日常开发使用 Debug。构建不会自动部署，产物为 `AICResourceKit/bin/Debug/AICResourceKit.dll`。不要对可能被游戏符号链接引用的 Release 产物做日常试验；游戏部署属于单独的验证步骤。

进行游戏验证前，退出游戏并检查安装目录内 `AICResourceKit.dll` 是普通文件还是符号链接。普通文件需要复制本次构建产物；符号链接需要确认其目标与本次构建配置一致。若部署明确使用 Release，则在部署步骤构建 `dotnet build AICResourceKit/AICResourceKit.csproj -c Release -m:1 -nr:false`。重新启动后再采集诊断，不能用旧进程或旧报告判断新代码是否生效。

## 模块职责

| 模块 | 职责 |
| --- | --- |
| `AICResourceKit/Contracts/` | 无 Unity 依赖的 v2 字段语义、目标身份、声明依赖及地址草案；由插件和工具链接编译 |
| `ReplacementCatalog` | 扫描清单、解析单个包、检查文件依赖和同步配置行 |
| `ReplacementSelection` | 在目录快照上处理包顺序、目标选择与授权，不读取磁盘 |
| `ReplacementPreparation` / `ReplacementWork` | 后台读取与合成、取消和结果交接，不创建 Unity 对象 |
| `ReplacementRuntime` | 主线程调度、目录接受和选择变更；具体资源处理见下表 |
| `SpineComposer` | 分层组合骨架、皮肤、动画及兼容映射 |
| `ReplacementResourcePaths` / `IO` / `Keys` | 路径边界、明文与密文读取及格式兼容 |
| `MtiResourceAddress` / `ReplacementMtiImagePatch` | 直接 MTI 图片的容器键、加载与释放入口；单图容器仍走已有入口 |
| `ReplacementSpriteLayout` / `ReplacementResourceReleasePatch` | Sprite 几何兼容检查与创建、Resources 卸载转交 |
| `ReplacementDiagnostic*` | 可关闭的观察器与报告，不改变目标匹配 |
| `PortraitControl*` / `PortraitReplacementPreview` | 主界面立绘控制、预览及会话状态 |
| `AICResourceKit.ResourceEncryptor/` | 命令行参数、严格包解析、依赖检查与加密导出 |

运行时文件位于 `AICResourceKit/Patches/ReplaceTexture/`，保留一个协调入口，用 `partial` 按职责组织现有共享状态：

| 文件 | 维护范围 |
| --- | --- |
| [ReplacementRuntime.cs](../AICResourceKit/Patches/ReplaceTexture/ReplacementRuntime.cs) | 初始化、目录刷新、选择变更及公共授权判断 |
| [ReplacementRuntime.Spine.cs](../AICResourceKit/Patches/ReplaceTexture/ReplacementRuntime.Spine.cs) | 主立绘准备、安装、释放及状态 |
| [ReplacementRuntime.SpineAssets.cs](../AICResourceKit/Patches/ReplaceTexture/ReplacementRuntime.SpineAssets.cs) | 创建 Spine Unity 资源及对象所有权 |
| [ReplacementRuntime.Viewers.cs](../AICResourceKit/Patches/ReplaceTexture/ReplacementRuntime.Viewers.cs) | 消费者重绑、动画与皮肤延续、显示参数 |
| [ReplacementRuntime.Mti.cs](../AICResourceKit/Patches/ReplaceTexture/ReplacementRuntime.Mti.cs) | 单图容器、直接图片的缓存记录、应用及释放 |
| [ReplacementRuntime.Resources.cs](../AICResourceKit/Patches/ReplaceTexture/ReplacementRuntime.Resources.cs) | Resources.Load 首载与刷新 |
| [ReplacementRuntime.Textures.cs](../AICResourceKit/Patches/ReplaceTexture/ReplacementRuntime.Textures.cs) | 共用的纹理准备、上传节流、原位更新及原图恢复 |
| [ReplacementPreviewResources.cs](../AICResourceKit/Patches/ReplaceTexture/ReplacementPreviewResources.cs) | 临时立绘预览资源与会话衔接 |

新逻辑放入对应职责文件，避免继续扩大调度入口。只有出现明确复用需求时再抽象公共类或接口。

## 关键行为

- 所有 Unity 对象创建、绑定、销毁和运行时状态修改在主线程执行；后台任务只准备数据。
- 直接 `MTI.LoadImage` 首载同步应用；后续更新 `MImage.Tx` 时游戏会同步缓存材质。MTIOneImage 内部的同名调用由原单图入口管理，避免重复应用。
- `Resources.Load` 首次返回的对象可能被游戏长期持有，需要同步首载；两个重载嵌套时识别已经返回的替换对象。后续刷新和关闭后恢复原图保持引用；`Resources.UnloadAsset` 将替换对象的卸载转交原资源并清理自建对象。
- Sprite 使用原整张纹理与原网格。打包、旋转或 UV 不能按原 rect/pivot/PPU 重建时明确拒绝，不能退化为看似成功的 FullRect 图片。
- 清单先识别目标身份，再检查内容。身份重复使整个包无效；单个目标的字段或依赖出错可隔离，并保留其他有效目标。
- 保留 v2 身份、包优先级、MTI null/空字符串语义和 Sensitive 开关行为。修改字段规则时同步更新 Schema、向量与契约说明。
- 关闭替换后恢复原对象、释放自建 Unity 对象属于正常生命周期。不要扩展成磁盘备份、快照或复杂回滚系统。

## 测试与日常修改

已有测试位于 `AICResourceKit.Test/`，采用 xUnit 的 `[Fact]`、`[Theory]` 和 `Assert`。先查相关测试，优先扩展已有用例；验证输入输出、选择结果或错误隔离，不为单纯移动文件编写测试。

保留正常流程、必要边界和失败提示的验证。修复缺陷时补充一个能够复现问题的用例；没有新行为的重构用现有覆盖确认兼容即可。避免重复用例、只断言“没有异常”的空测试和为罕见场景堆积框架。

涉及 Unity 加载或显示的修改，自动测试之后还需使用一个目标包在游戏中检查加载、刷新、关闭和画面。尚未进行游戏验证时，明确记录这一限制，不以静态方法签名测试代替实际结果。

## 文档维护

每次实现或修改功能时，在开发过程中同步维护以下正式文档：

| 修改内容 | 同步维护 |
| --- | --- |
| 安装、配置、热键、操作步骤 | [使用说明](usage.md)，必要时更新根 README |
| 资源包字段、依赖或兼容规则 | [资源包参考](resource-packs.md)、[资源契约](resource-contract.md)、Schema 与向量 |
| 新命令或公共 API | [使用说明](usage.md)中的可运行示例 |
| 诊断字段、入口或已验证能力 | [诊断说明](diagnostics.md)及对应调查记录 |
| 构建方式或模块职责 | 本文 |
| 计划进展或仍未实现的能力 | [资源工具计划](planning/noel-resource-tools.md)，与已支持功能分开 |

正文描述当前可执行的操作、参数、结果和限制；不保留对话、交付过程或临时测试日志。命令从仓库根目录执行，示例避免写死开发者的本机路径，文档链接使用仓库内相对路径。更名时更新入口和交叉引用。

提交修改前，检查涉及的示例、链接和 UTF-8／空格缩进／CRLF 格式。项目 `.editorconfig` 提供默认格式；保留未修改代码的原有风格，避免全仓库格式化。正式文档由 Git 跟踪，与代码一起评审。
