# 从 BetterExperience 迁移到 AICResourceKit

## 代码归属

| 内容 | 新位置 |
| --- | --- |
| 资源替换、Spine 合成、加密读取、加载诊断 | `AICResourceKit/Patches/ReplaceTexture/` |
| 立绘姿态与资源预览 | `AICResourceKit/Patches/Portrait*.cs` |
| 资源配置与刷新热键 | `AICResourceKit/BConfigManager/` |
| 立绘控制界面 | `AICResourceKit/BControlManager/ControlManagerPortrait.cs` |
| 加密命令行工具 | `AICResourceKit.ResourceEncryptor/` |
| 回归测试 | `AICResourceKit.Test/` |
| P01 静态调查结果 | `docs/resource-replacement/ver030g-investigation.json` |

BetterExperience 已移除上述功能及入口，保留马赛克、污渍、移动、战斗等原有功能。AICResourceKit 不引用 BetterExperience 程序集或项目。

## 游戏目录与配置

迁移资源目录和插件时，先退出游戏，再执行下列步骤：

1. 将 BetterExperience 更新为已移除资源替换功能的版本，避免仍使用含旧资源钩子的 DLL。
2. 安装 `AICResourceKit.dll` 至 `BepInEx/plugins/AICResourceKit/`。
3. 将原 `BepInEx/plugins/BetterExperience/ReplaceTexture/` 的内容复制到 `BepInEx/plugins/AICResourceKit/ReplaceTexture/`，保持子目录结构。
4. 启动一次生成 `AICResourceKit.cfg`，在新插件配置页恢复下表设置。可退出游戏后复制同名配置条目的值，不要直接覆盖整个新配置文件。

| 原配置项 | 新配置项 |
| --- | --- |
| `Texture.EnableResourceReplacement` | 同名同义，默认关闭 |
| `Texture.EnableSensitivities` | 同名同义，默认开启 |
| `Texture.EnabledReplacementPacks` | 同名同义，保留包 ID、启用值和列表顺序 |
| `Texture.EnableResourceDiagnostics` | 同名同义，默认关闭 |
| `Texture.ResourceDiagnosticFilter` | 同名同义 |
| `Hotkey.FlushTextureHotkey` | 同名同义，默认 `Ctrl+T` |
| `General.EnableBetterExperience` | 新插件使用独立的 `General.EnableMod` |

新插件不自动读取旧配置或旧资源目录。日志位于 `BepInEx/plugins/AICResourceKit/logs/`，诊断报告为其中的 `resource-diagnostics.json`。立绘控制是会话状态，不写入存档。

## 格式兼容

`.replacement.json` 保持 v2，目标标识、包 ID、层叠顺序及相对依赖路径保持原语义。BEREENC v1 文件头、密钥编号和加解密参数保留，已有密文无需重新加密。

加密工具的新命令：

```powershell
dotnet run --project AICResourceKit.ResourceEncryptor/AICResourceKit.ResourceEncryptor.csproj -- encrypt --input "D:/Assets/ReplaceTexture" --output "D:/Assets/EncryptedReplaceTexture"
```

## 实现进度

P01 的调查、默认关闭的诊断和测试已迁入；标题入口的实机采集与画面验证见[诊断说明](diagnostics.md#验证范围)。后续已完成 P02 公共契约，见 [资源目标与清单契约](resource-contract.md)；P03 已补充直接 MTI 入口和未打包 Sprite 处理，实机验证与待查入口状态见[资源工具计划](planning/noel-resource-tools.md)；P04 已增加 PXL 图片/整页替换，见 [PXL 说明](pxl-replacement.md)；P05 已增加普通 SpineViewer、剧情 JSON 和显式多页映射，见[剧情 Spine 说明](spine-viewer-replacement.md)；P06 已增加独立图集区域/整页与 PICT 图片入口，见[图集说明](atlas-replacement.md)，实际消费者验证边界见[诊断说明](diagnostics.md)；P07 已补充 MPCC 原生解码报告、五个文件调查与 PXL 依赖映射，见[MPCC 说明](mpcc-inspection.md)；P08 视频替换暂缓；P09 已统一刷新、授权撤销和加载结果，新增[资源状态说明](resource-status.md)，现有配置键与 v2 包保持兼容；P10 已完成统一依赖检查、独立样例和接口定版，见[第一阶段交付](phase-one-delivery.md)。

后续实施顺序见[资源工具计划](planning/noel-resource-tools.md)。静态调查 JSON 保留采集时的 `pluginVersion=2.1.1`，它是历史证据元数据；当前插件版本为 `1.1.0`，可安装契约版本为 `1`。
