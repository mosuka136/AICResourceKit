# 资源加载诊断

适用基线：`ver030g`。诊断用于观察实际加载参数、检查资源包是否被发现，以及区分准备、应用和失败阶段。默认关闭，不会增加新的资源替换入口。

机器清单见 [ver030g-investigation.json](resource-replacement/ver030g-investigation.json)：逐项登记 141 个已确认资源组、9 个待确认资源组，并另外保留两个已排除视频。`sgwall.pxls`、`syabon.pxls`、`cane_bermit.pxls` 单独标注为仅装备部件。角色归属沿用目录已有审图结论，源码调查不代替审图。

调查直接使用游戏的 `Assembly-CSharp.dll`、`unsafeAssem.dll`、`pixelliner.dll`，已确认与插件引用程序集一致。静态清单保存程序集 SHA-256，运行时报告保存实际加载程序集 MVID。游戏原件、完整反编译源码和本机调查中间文件不纳入版本管理。

## 当前能力与验证范围

| 路径 | 当前替换能力 | 证据与限制 |
|---|---|---|
| 主立绘 `SvTexture` / `SpineViewerNel` | 既有 v2 单页 Spine | 通用 `key + jsonKey`，没有四姿态白名单；37 组均登记，逐组实机未验证 |
| `MTI.LoadContainerOneImage` | 既有 v2 主纹理 | PXL 省略 `image_key`，目标保持空值；`load_key` 是持有者标记 |
| `MTI.LoadImage` | v2 直接图片替换 | 按容器和图片键匹配，更新 MImage 及缓存材质；ver030g 已验证 key_noel、difficulty 的首次加载与画面；刷新、关闭和释放仍待实机核对 |
| `Resources.Load` | v2 Texture2D / 未打包 Sprite | Sprite 保留原网格与 UV；打包/旋转布局拒绝。两个目录 Sprite 的实际消费入口仍未确认 |
| PXL 内嵌、打包页和额外页 | v2 `loader: pxl` 图片与整页替换 | MTI 来源、原始 ID/I/P、外部槽位或打包页序号定位；共享纹理原位更新，逐页报告结果 |
| 普通 `SpineViewer` / Fatal | 本项只观察资源加载 | 不经过主立绘适配器；部分图片经过 MTI 不代表骨架已接入 |
| PICT / EF_PICT | 本项只观察区域查找 | `SpvLoader.GetImage` 直接取 atlas 区域，能够使用默认骨架以外的图片 |
| MPCC | 本项只观察解码器 | 已知编辑器读取器及 PXL 页组合机制；五个文件的游戏调用和角色归属待查 |
| VideoClip | 本项只观察播放器状态 | 教程为 `Tuto_mp4/<clip>` → `FillBlockMovie`；地图效果为 `Effect_mp4/<clip>` → `M2UnstbMovie` |

“只观察”不表示资源包可以使用相应新类型。观察器不更换资源、不触发剧情、不读取服装任务，不改变替换授权和优先级。静态清单不需要复制进游戏，运行时工具不依赖 wardrobe。

## 关键参数与待查项

| 来源 | 参数与共享关系 |
|---|---|
| `stand_battle.old` | `key=stand_battle`，`jsonKey=stand_battle.old`；MTISpine 按 JSON 缓存 |
| `noel.pxls` | `asset_key=PxlNoel/noel.pxls.bytes.texture_0`，`image_key=null`；PxlCharacter、MImage 和材质共同消费 |
| `_icons.pxls` | `MTRX.loadMtiPxc("_icons", "Pxl/_icons.pxls", "_", false, false)`，明确关闭外部图片加载 |
| `_icons_ttr.pxls` | `MTI_mgm_ttr.Load<TextAsset>("_icons_ttr.pxls")` → PxlsLoader；绕过 MTRX |
| `fatal_nusi_1` | JSON 为 `fatal_nusi_1`，文本容器为 `Fatal/fatal_nusi_0.atlas`，图片容器/键为 `Fatal/fatal_nusi_0` / `fatal_nusi_0` |
| `noel_peeping__0000/0001/0002` | 来自 `Fatal/cuts_nightingale_0_0.atlas`；PICT 图片和骨架共用页 |
| 三个教程视频 | 容器为 `Tuto_mp4/` 加各自名称，clip 键分别为 `tuto_energyball`、`tuto_watershard`、`tuto_watershard07` |

`MTI.resources_path` 由构造器按 `Assets/Editor/AssetBundlesSrc/<原始 key>/` 保存。观察器去除固定前后缀取回参数，不从磁盘名或 `Texture.name` 推断身份。PXL 的 `external_png_header` 只记录实际字段值，可能仍为默认值，不视为已定版的容器身份。

- `wplmode_` 位于 `mti_title_wpl.dat`，基线 ver030g 程序集未定位到对应 `LoadImage` 调用。通用匹配测试中的该键只验证身份隔离，不能证明这个入口已经执行；需继续检查标题分支与动态键。
- `noel_bassrobe.pxls` 含内嵌图片，但不在本基线 `MTR.Anoel_pxls` 中，实际调用及加载条件待查。
- `mgm_bun.pxls.bytes.texture_0` 的 Texture2D 依赖经过小游戏 PXL 路径，但同包 Sprite 的使用没有证据。`damage_backvoreenemy` Sprite 和 `.atlas` 同样保留待查，不伪造骨架 JSON。
- 主纹理加载不能证明 part、mask、混合页均已支持。`MTIOneImage.ReplaceExternalPngForPxl` 依赖包内对象数量，数量为 2 时通过 `_1` 加载额外图片；含 Sprite 的包尤其需要核对。
- MPCC 的已知解码器由编辑器文件选择器调用，不能推断游戏会自动读取 `StreamingAssets/mobpcc`。五个文件继续保留独立待确认状态。
- 80 组 PXL 外部主纹理键来自目录候选与 MTRX 通用公式。未逐项观察实际参数、异步到达和旧页消费者；使用时必须同时读取各组 `evidence` 和 `pendingReasons`。

P03 对 `SceneTitleTemp.prepareMti` / `initTitleLogo`、`UiTitleDifficultyConfirm` 和 `MImage.Tx` 的源码复核确认：`key_noel`、`difficulty` 使用 `MTI_title` 容器，并将 `MImage.getMtr()` 的材质交给绘制器。`Tx` 赋值会同步改写这些缓存材质的主纹理。直接加载入口与单图容器分开登记，容器 `Dispose` / `UnloadAll` 前清理替换。

两个待确认 Sprite 的导出元数据也不能当作 Resources 地址：`mgm_bun.pxls.bytes.texture_0` 的逻辑 rect 为 512 × 1024，实际裁切高约 641；`damage_backvoreenemy` 的逻辑 rect 为 384 × 968，网格边界另有细小裁切。重建时保留逻辑画布和原网格，不能使用裁片 PNG 的尺寸推导画布。尚未找到这两个 Sprite 的直接消费者，因此没有增加猜测性的专用入口。

机器调查清单保留 P01 采集时的状态，不回写为运行成功。P03 的当前使用方式见[使用说明第 2.5、2.6 节](usage.md)。

方法签名、读取参数、缓存对象、消费者与释放时机详见机器清单的 `routes`。每组 `sources` 保留对象 ID、容器、序列化文件及字符串形式的 64 位 pathId。共享对象可出现在多组，一组也可关联多个目标。每组最多保留 6 条来源引用位置，`referenceCount` 为完整去重后的引用数。

## 使用方法

1. 使用 AICResourceKit Debug 插件，在 AICResourceKit“贴图”配置页设置 `ResourceDiagnosticFilter`。
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

新适配器使用 `PXL image/page / ReplacementRuntime` 入口，`details.address` 可直接复制到 `loader: pxl` 目标中，尺寸来自当前原纹理。`image-registered` 表示图片已登记；`page-awaiting-texture` 表示外部页仍待到达；`shared-texture-updated` 表示共享纹理完成像素上传。`address-not-found` 表示已解码的来源中没有对应图片角色或页。

同一纹理可能有多个地址，诊断会逐个列出，`sharedAddresses` 表示其别名数量。逐项检查成功、失败与待到达状态，不能用一页成功概括整套 PXLS。P01 旧观察器输出保留；制作新包应使用带 `details.address` 的适配器记录。操作见 [PXL 说明](pxl-replacement.md)。

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

检查安装目录内 DLL 的符号链接目标及构建配置，部署后重启游戏；操作见[安装说明](usage.md#11-安装插件)。重新采集当前会话报告，正常应用会出现 `candidate-applied`，结果为 `mimage-texture-assigned`；失败时查看 `candidate-failed` 的原因及插件日志。

## 验证范围

自动测试覆盖诊断筛选、聚合、导出、错误处理、方法签名和调查清单。构建环境、引用 DLL 与测试命令见[开发说明](development.md)。

ver030g 已使用 `sakura-furisode-title-v1` 验证 `MTI_title/key_noel` 和 `MTI_title/difficulty`：两项均记录 `candidate-applied / mimage-texture-assigned`，标题和难度选择界面显示对应替换图片。该验证覆盖首次加载和画面，不包括刷新、关闭与释放。

ver030g 的 PXL 运行时验证覆盖 `PxlNoel/noel.pxls` 外部页、`PxlNoel/noel_bassrobe.pxls` 内嵌打包页、`MTI_mgm_ttr` 中的 `_icons_ttr.pxls` 和 `Pxl/_icons.pxls`。混合内嵌/外部页及额外页夹具验证了替换、刷新、停用恢复、延迟重绑、共享释放及单页错误隔离；同时检查原纹理引用、不可读和压缩纹理、mipmap、RenderTexture 与字体初始化。该检查基于实际 Unity 对象和像素，不替代全部动作、剧情及场景画面验收。

P01 静态清单保留采集时的状态和计数，不自动同步后续实机验证。其他目标仍需在对应场景采集实际报告并检查画面；静态调查、方法签名与 `candidate-applied` 均不能独立证明画面正确。
