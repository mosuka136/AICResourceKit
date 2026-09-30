# 资源加载诊断

诊断观察实际加载参数、消费者及替换事件，用于定位目标和排查失败。默认关闭，不改变目标匹配或资源授权。支持类型、基线地址和未确认场景见[兼容性说明](compatibility.md)。

需要了解当前哪些包生效时，先看[资源状态](resource-status.md)；需要追踪加载入口与历史事件时，再开启本文的持续诊断。

## 使用方法

1. 安装与当前游戏版本兼容的插件，在 AICResourceKit“贴图”配置页设置 `ResourceDiagnosticFilter`。
2. 开启 `EnableResourceDiagnostics`，默认值为 `false`；模组总开关也必须开启。调查原始加载无需开启资源替换。
3. 正常进入目标界面、姿态或剧情。记录首次标题加载时，在启动游戏前设置配置。
4. 读取 `BepInEx/plugins/AICResourceKit/logs/resource-diagnostics.json`。
5. 调查结束后关闭诊断。

有新记录时每 5 秒覆盖报告，关闭诊断或停用插件时也导出；刷新贴图热键额外触发一次导出。更改筛选或重新开启建立新会话并覆盖报告。开启前的历史调用不会被补写为命中，已有资源包目标仅列为 `discovered`。

筛选对加载器、容器、资源键、对象类型、既有 v2 身份执行忽略大小写的包含匹配。分号分隔多个条件，满足任一个即可；留空记录全部。例如：

```text
stand_battle;PxlNoel/noel.pxls;Tuto_mp4
```

筛选不改变实际资源匹配的大小写和空值规则。重复的“目标、阶段、入口、结果”聚合为计数与首次/最后时间，每会话最多 4,096 种观察；超出时 `droppedObservations` 记录丢弃次数，应缩小筛选。打包页保留最多 16 个图片 ID 样本，另有完整区域数和 `sampleTruncated`，不是完整图片导出器。

报告先写同目录临时文件，再原子替换，避免读取半份 JSON。当前会话继续运行时，写入失败会保留内存记录并重试，不中断资源加载；若结束或切换会话时仍无法写入，最新记录不会落盘，原因见日志。报告包含参数、包 ID 和错误原因，不包含图片或解密内容。

## PXL 地址与结果

PXL 适配器使用 `PXL image/page / ReplacementRuntime` 入口，`details.address` 可直接复制到 `loader: pxl` 目标中，尺寸来自当前原纹理。`image-registered` 表示图片已登记；`page-awaiting-texture` 表示外部页仍待到达；`shared-texture-updated` 表示共享纹理完成像素上传。`address-not-found` 表示已解码的来源中没有对应图片角色或页。

同一纹理可能有多个地址，诊断会逐个列出，`sharedAddresses` 表示其别名数量。逐项检查成功、失败与待到达状态，不能用一页成功概括整套 PXLS。制作资源包应使用带 `details.address` 的适配器记录，不从通用加载观察中的文件名推导地址。操作见 [PXL 说明](pxl-replacement.md)。

## 图集区域与整页结果

独立图集使用 `Atlas / ReplacementRuntime` 入口。`details.address` 可复制到对应 `atlas-region` / `atlas-page` 目标，`details.pageKey` 说明区域所在的页。`shared-page-bound` 表示原页纹理已绑定，`picture-region-bound` 表示 PICT 路径查询了区域；`shared-atlas-texture-updated` 表示上传完成。来源来自实际 MTI 文本或 Resources.Load，不从纹理名称推导。

`invalid-address-or-layout` 表示区域/页或原布局不适用；`page-rejected` 表示尺寸、重叠或跨入口冲突等导致该页拒绝，原因见 `reason`。停用或失败时恢复原页像素。运行时会将新诊断会话中的已登记页列出，仍需实际查询才能看到具体 PICT 区域记录。

## MPCC 文件与图片输入

控制页可手动导出 `logs/mpcc-inspection.json`，无需开启持续诊断，步骤见[MPCC 说明](mpcc-inspection.md)。它列出实际文件、摘要、内部角色与部件；仅对已确认加载来源的 PXL 输出可安装地址。

持续诊断的 `palette-read` 表示游戏解码调用，仍不带可靠文件路径；`inspection-decoded` 是报告工具主动读取，不能算自然游戏调用。`pcc-source-bound` 来自 `MobPCCContainer.AddChr`，列出实际角色输入槽位 0（原色）和 1（部件遮罩）的 PXL 地址与加载状态。生成后的调色 RenderTexture 是缓存结果，需要游戏重新应用调色才能消费已更新的输入。

## 状态语义

诊断 `reportVersion=1` 与资源包 `formatVersion` 无关。直接 MTI 图片使用既有 v2 身份；尚未接入替换的新观察地址仍为 `runtimeIdentity=null`，不提前定义新包类型。

| 字段或阶段 | 含义 |
|---|---|
| `hooks[].status=installed` | 补丁已注册，不表示入口已执行 |
| `hooks[].status=unavailable` | 方法缺失、注册失败或原有补丁未注册 |
| `discovered` | 清单发现目标，游戏可能尚未加载 |
| `entry-hit` | 实际经过入口，`outcome` 区分返回对象、缺对象或播放器状态 |
| `candidate-pending` | 准备中，或组合已安装但等待绑定 |
| `candidate-applied` | 原有实现完成相应对象绑定/资源对象准备，不证明全部消费者或画面正确 |
| `candidate-failed` | 清单、准备或应用失败，保留错误类型与原因 |
| `restored` / `released` | 恢复、释放或关闭请求，具体行为见 `outcome` |
| `visualVerification=not-performed` | 工具不宣称人工画面验收通过 |

应用、失败、恢复和释放分别保留，历史成功不能证明当前仍在显示。MPCC 解码参数没有文件路径，内容名称不能替代文件身份。视频 `hasClip` 与内部状态不代表已经观看或验证循环、结束。`not-observed` 不等于资源不存在或已排除。

### 已发现标题资源包但没有替换记录

对于 `MTI_title/key_noel` 和 `MTI_title/difficulty`，如果只有清单 `discovered` 和 `MTI.LoadImage` 的 `entry-hit`，先检查已启用资源替换及对应包，并确认游戏实际加载的 DLL 已包含直接 MTI 图片替换功能。旧版本诊断钩子也能记录 `entry-hit`，这本身不能证明替换入口已接入。

检查安装目录内 DLL 的符号链接目标及构建配置，部署后重启游戏；操作见[安装说明](usage.md#安装插件)。重新采集当前会话报告，正常应用会出现 `candidate-applied`，结果为 `mimage-texture-assigned`；失败时查看 `candidate-failed` 的原因及插件日志。

## 当前状态与事件历史

“控制界面 → 资源调查”提供每秒更新的加载结果和手动导出的 `resource-status.json`，无需开启持续诊断。其计数来自当前消费者与资源绑定；本页的 `resource-diagnostics.json` 保留会话事件，两者用途和字段不同。先使用[资源状态说明](resource-status.md)检查未授权、未加载、应用失败及具体文件，再按需采集历史。

## 解读范围

报告保存实际调用参数、事件计数和程序集标识，不附带游戏原图或解密内容。钩子已安装、方法签名存在、显式调用原生对象、自然场景命中及最终画面分别代表不同证据，不能相互替代。

普通 Spine 查看器的 `details.address`、`pageNames` 和 `packageIds` 描述实际来源及候选；`viewer-source-bound` 表示消费者已登记。直接 MTI 图片按真实容器和图片键识别，并通过 `MImage.Tx` 更新缓存材质。不能从导出目录名、`Texture.name` 或临时 instance ID 构造运行时身份。

其他字段与公共报告集成方式见[工具集成](integration.md#运行时报告)。
