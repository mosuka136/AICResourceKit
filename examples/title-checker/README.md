# 独立标题测试包

适用游戏：`ver030g`。适用插件：AICResourceKit 1.1.0。本样例不需要 wardrobe，也不包含游戏原图；`checker.png` 是程序生成的 1280 × 1520 RGBA 棋盘格。

## 安装与检查

1. 将本目录复制到游戏的 `BepInEx/plugins/AICResourceKit/ReplaceTexture/title-checker/`。
2. 开启资源替换，启用 `aicresourcekit-title-checker-v1` 并放在其他标题资源包之后。该包只替换 `MTI_title/key_noel`。
3. 进入标题界面。原角色图片所在区域应显示青色与紫色棋盘格；它用于确认加载，不能作为服装成品。
4. 在“控制界面 → 资源调查”按包 ID 筛选，应看到 MTI 目标已应用。导出状态后，检查对应 `runtimeIdentity` 与 `appliedPackageIds`。
5. 停用样例包，应恢复其他仍启用的标题包或原图片。游戏存档不受影响。

编辑 `checker.png` 时保留原尺寸，按 `Ctrl+T` 或触发“刷新资源”，检查显示更新。更新插件 DLL 后需要重启游戏。

## 检查与加密

从仓库根目录执行，输出目录必须尚不存在：

```powershell
dotnet run --project AICResourceKit.ResourceEncryptor -- inspect --input examples/title-checker
dotnet run --project AICResourceKit.ResourceEncryptor -- encrypt --input examples/title-checker --output examples/title-checker-encrypted
```

检查报告只列出清单和 PNG 两个文件。README 不被加密导出。密文包使用相同 ID，测试时将明文目录移出资源根目录，再安装密文目录，避免同时发现两个同 ID 包。

不能用此样例证明其他场景或所有资源类型均已验收；完整接口和验证范围见[第一阶段交付说明](../../docs/phase-one-delivery.md)。
