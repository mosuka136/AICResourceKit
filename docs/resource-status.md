# 资源状态、刷新与错误排查

在 UnityModBase 的 AICResourceKit **控制界面 → 资源调查**查看“加载结果”。本页适用于主立绘、MTI、Resources、PXL、普通剧情 Spine 与独立图集；视频替换尚未实现，MPCC 调查不作为替换目标。

## 日常操作

1. 在配置页开启“启用资源替换”，在资源包列表启用需要的包。列表越靠后优先级越高，Sensitive 目录还受“启用敏感资源”控制。
2. 进入目标资源实际使用的场景，在资源调查页查看结果。状态每秒更新；同一个目标可以对应多个已加载消费者。
3. 修改资源文件后触发“刷新资源”，或按默认 `Ctrl+T`。刷新会取消尚未应用的旧准备结果，扫描完成后按当前选择重新准备。
4. 需要排查或保存结果时，触发“导出资源状态”，读取游戏目录的 `BepInEx/plugins/AICResourceKit/logs/resource-status.json`。

“状态筛选”按包 ID、目标或错误文本进行不区分大小写的包含匹配，仅影响显示。清空即可显示全部结果。加载结果中的显示值不控制资源包开关；包启停与顺序仍在配置页修改。报告总是导出全部结果，下一次导出覆盖同名文件。

停用包、关闭替换和撤销敏感授权在下一次主线程配置检查时生效，不等待后台读取完成。若撤销了当前最高优先级包，先恢复原资源，再准备仍启用的候选。启用和调整顺序会合并约 0.15 秒内的连续变化。

扫描与准备期间，可继续显示仍获授权的上一份资源。首次加载需要立即返回给游戏的对象，继续按已接受目录同步准备；刷新完成后再更新。主立绘 Spine 首次显示或切换到新的条件状态时，也在主线程同步准备，避免先显示原版。普通图片、无立绘条件的 PXL、图集和剧情 Spine 不触发主立绘预览。主立绘 Spine 与带 `portraitSelection` 的 PXL 支持[短暂预览与恢复](resource-packs.md#启用资源包)。

## 状态含义

| 显示 / JSON 值 | 含义与处理 |
| --- | --- |
| 未启用 / `disabled` | 总替换开关或相关包未启用 |
| 未授权 / `unauthorized` | Sensitive 包尚未获授权；授权关闭时不解析其目标内容 |
| 不支持 / `unsupported` | 清单版本、目标类型或加载器不支持；包括视频目标 |
| 失败 / `failed` | 文件、准备或应用出错；阅读同项的包、目标和原因 |
| 准备中 / `preparing` | 存在尚未完成的候选；原有授权资源可能仍在显示 |
| 已应用 / `applied` | 当前观察到的该目标消费者均已安装候选 |
| 部分应用 / `partial` | 同一目标只有部分已加载消费者安装了候选 |
| 已加载，未应用 / `loaded` | 已观察到原资源，但当前没有该身份的候选；检查选择、覆盖关系和共享目标 |
| 未加载 / `not-loaded` | 尚未观察到目标消费者；进入相应场景后再检查，不能据此证明地址正确 |

状态描述对象绑定和像素处理，不证明最终画面已验收。运行失败时，仍获授权的 MTI、Resources 或 Spine 旧候选可能被保留，此时会同时出现 `failed` 和非零 `appliedCount`。包级 `failed` 表示该清单包含错误，不意味着其他有效目标全部失败。

同一 PXL 物理纹理不能被不同图片/页目标同时覆盖；图集的重叠区域或整页与区域同时选择也会报错。PXL 与图集若同时选中同一物理纹理，两路均拒绝并恢复原图。停用其中冲突的目标后重新刷新，其他独立纹理继续处理。不同入口的优先级不会绕过这些共享纹理限制。

## 当前状态报告

`resource-status.json` 无需开启持续诊断；它读取当前运行时登记表，不用历史成功事件代替当前状态。路径遵循诊断报告的脱敏规则。

| 字段 | 含义 |
| --- | --- |
| `reportVersion` / `evidenceKind` | `1` / `current-resource-state` |
| `pluginVersion` / `exportedUtc` | 插件版本、UTC 导出时间 |
| `capabilities` | 当前 DLL 的编译能力表，与 CLI `capabilities` 同源；不代表本次运行已经验证这些能力 |
| `selectionRevision` | 当前已接受选择的会话序号，不是清单版本 |
| `replacementEnabled` / `allowSensitive` | 当前开关 |
| `refreshing` / `selectionPending` | 是否正在扫描；配置是否还在等待合并应用 |
| `scanError` | 最近一次目录扫描失败原因；为空表示无此类错误 |
| `enabledPackageIds` | 配置中启用的包，保留优先级顺序 |
| `packages` | 已发现清单的 ID、路径、敏感标记、启用标记及包级状态 |
| `targets` | 成功解析的目标，按统一运行时身份合并 |
| `issues` | 无法解析或不支持的清单/目标，含包 ID、清单、零起始 `targetIndex`、可获得的身份、错误码、原因及依赖文件 |
| `visualVerification` | 固定 `not-performed`；需要另行观察实际画面 |

每个 `targets` 项包含目标地址、`runtimeIdentity`、`type`、`status`、`loadedCount`、`appliedCount`、`preparingCount`、`appliedPackageIds`、`selectedPackageIds`、`declarations` 和 `errors`。`declarations` 定位声明它的包、清单与目标索引，并通过 `files` 列出图片、atlas、JSON 和显式页文件；`selectedPackageIds` 是按该身份选中的候选，`appliedPackageIds` 是实际安装的包，两者在准备、失败、预览或共享覆盖期间可能不同。

尚未授权的敏感包仅出现在 `packages`，不会泄露未读取的目标。无法识别身份的错误出现在 `issues`，不编造目标地址。缺失文件使用 `missing-dependency`，其余常见代码为 `unsupported-version`、`unsupported-target`、`invalid-target`、`invalid-manifest`。

## 常见问题

- 包存在但未生效：先看全局开关、包状态及 Sensitive 授权，再看目标是否已加载、实际应用包是否被更高优先级覆盖。
- 修改后还是旧图：刷新并等待准备结束；失败时查看具体文件。刷新只重读资源，不加载新插件 DLL，更新 DLL 后需要重启游戏。
- 一项失败：按 `declarations` 或 `issues` 定位清单的 `targets[index]`。单个目标错误保留其他有效目标；身份重复等包级错误会使整个包无效。
- 需要追踪加载入口：临时开启[资源加载诊断](diagnostics.md)，复现场景后检查 `resource-diagnostics.json`。该文件记录会话历史，与本页当前状态报告分开。
