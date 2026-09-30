# 第一阶段接口交付

插件版本 **1.1.0**，公共契约版本 **1**，游戏验证基线 **ver030g**。可安装清单保持 v2，密文保持 BEREENC v1；既有包无需改写或重新加密。第一阶段交付现有图片、PXL、Spine、图集适配器与调查工具。视频 P08 暂缓，第二阶段制作工具尚未接入。

## 交付入口

| 内容 | 文件或接口 |
| --- | --- |
| 字段、身份、依赖、优先级和错误规则 | [资源契约](resource-contract.md) |
| 作者字段校验与共同预期 | [Schema](resource-replacement/resource-replacement.schema.json)、[测试向量](resource-replacement/contract-vectors.json) |
| 编译版本的能力与限制 | [capabilities.json](resource-replacement/capabilities.json)、`ResourceCapabilities.Describe()` |
| 自动测试和实际验证范围 | [phase-one-verification.json](resource-replacement/phase-one-verification.json)、[诊断验证范围](diagnostics.md#验证范围) |
| 无游戏原图的独立样例 | [标题棋盘格](../examples/title-checker/README.md) |
| 声明文件枚举、检查与密文输出 | [inspect / capabilities / encrypt](usage.md#33-检查依赖能力与导出密文包) |
| 当前消费者、选中包、文件错误 | 游戏导出的 [resource-status.json](resource-status.md) |
| 实际加载参数、调用与共享消费者证据 | 游戏导出的 [resource-diagnostics.json](diagnostics.md) |
| MPCC 文件与 PXL 输入映射 | [MPCC 报告](mpcc-inspection.md) |

能力表描述编译版本支持什么。加载报告描述某次运行实际观察到什么。下游工具应同时读取二者；`supported: true` 不代表所有游戏场景都已验证。未命中的对象不能判为已排除，也不能凭资源名猜出实际地址。

## 当前支持范围

| 能力 | 可安装类型 | 主要边界 |
| --- | --- | --- |
| MTI 单图与直接图片 | `texture / mti` | 精确容器与图片键；保持原尺寸，兼容既有 null/空键含义 |
| Resources 图片与 Sprite | `texture / resources` | Texture2D、未打包 Sprite；保持纹理尺寸、网格及 UV；打包或旋转 Sprite 拒绝 |
| PXL 图片和页面 | `texture / pxl` | I/P 内嵌图片、外部和打包页；保持帧布局，冲突共享纹理拒绝 |
| 主立绘 | `spine` | Spine 4.1、单页，保留分段组合、兼容映射及临时预览 |
| 普通剧情 Spine | `spine-assets` | MTI/Resources 来源，显式多页；查看器材质独立，保留播放状态 |
| 独立图集与 PICT | `atlas-region / atlas-page` | 保留图集布局；区域输入仍为完整页 PNG，仅写选中矩形；重叠拒绝 |
| MPCC 调查 | 无替换类型 | 原生解码与 PXL 输入映射；不应用预设，不重算已生成调色纹理 |
| 视频 | 不支持 | P08 暂缓；仅观察诊断和地址草案 |

下列游戏地址或场景继续保留为第一阶段调查待办，不能标为已支持的具体目标：`wplmode_` 的真实调用参数、`mgm_bun.pxls.bytes.texture_0` 与 `damage_backvoreenemy` 的 Unity Sprite 消费入口、独立 `damage_backvoreenemy` atlas 的自然调用场景。Resources 多页与部分共享资源使用合成夹具验证；完整剧情、遮罩、镜头、全部动作与污渍画面尚未逐场景验收。

控制页的筛选、刷新、状态导出与 MPCC 导出已实际点击验证。此次 Windows 自动输入未能触发框架的 F1/F3 热键，借助 UnityExplorer 调试界面展开控制页后完成检查；这不计为热键通过，原因仍待核对。

## 独立复现

从仓库根目录执行；需要 .NET 8 SDK，构建插件的其他依赖见[开发说明](development.md)。

```powershell
dotnet build AICResourceKit.ResourceEncryptor/AICResourceKit.ResourceEncryptor.csproj -c Debug
dotnet AICResourceKit.ResourceEncryptor/bin/Debug/net8.0/AICResourceKit.ResourceEncryptor.dll capabilities
dotnet AICResourceKit.ResourceEncryptor/bin/Debug/net8.0/AICResourceKit.ResourceEncryptor.dll inspect --input examples/title-checker
dotnet AICResourceKit.ResourceEncryptor/bin/Debug/net8.0/AICResourceKit.ResourceEncryptor.dll encrypt --input examples/title-checker --output title-checker-encrypted
```

`inspect` 应列出一个目标、两个文件，身份为 `texture\nmti\nMTI_title\nkey_noel`。输出目录必须尚不存在。按[样例说明](../examples/title-checker/README.md)分别安装明文或密文目录，确认标题出现棋盘格、状态为已应用，刷新后仍显示，停用后恢复原图或当前顺序中的其他包。不要同时安装具有相同 ID 的明文和密文副本。

自动测试命令：

```powershell
dotnet test AICResourceKit.Test/AICResourceKit.Test.csproj -c Debug -m:1 -nr:false
python tools/validate-contract-schema.py
```

仓库中的样例与测试图像是合成资源。游戏原件、引用 DLL、个人配置和原始运行日志不进入交付；机器验证记录仅保留范围、结果与限制。

## 下游兼容规则

先检查 `contractVersion`、`manifestVersions` 与所需 `features[].supported`。解析字段、生成身份和枚举声明依赖可链接 `AICResourceKit/Contracts/`，也可用共同测试向量校验其他语言实现；文件存在、解码及 Sensitive 边界使用 CLI `inspect`。清单声明与运行时实际加载来源分别处理，不能把目录名当作容器键。

契约 1 固定现有可安装语义；视频及 `ResourceAddressDraft` 仍属草案。报告消费者应忽略未知附加字段；改变既有字段含义或结构时升级对应版本。`contractVersion`、资源包 `formatVersion`、各报告 `reportVersion` 与插件版本用途不同，不要求数值一致。
