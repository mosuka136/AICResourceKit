using AICResourceKit.BLogSpace;
using AICResourceKit.BPatchGUI;
using AICResourceKit.Patches.ReplaceTexture;
using System;
using UnityModBase.HTranslatorSpace;
using UnityModBase.HControlSpace;

namespace AICResourceKit.BControlManager
{
    internal static partial class ControlManager
    {
        private static void InitializeResources()
        {
            try
            {
                BService.Control.CreateTable("Resources", new Translator("资源调查", "Resources"));
                Bind("Resources", "StatusFilter", () => ReplacementRuntime.StatusFilter,
                    value => ReplacementRuntime.StatusFilter = value ?? "",
                    new Translator("状态筛选", "Status Filter"),
                    new Translator("按包 ID、目标或错误信息筛选显示；不改变包选择。", "Filter results by pack, target or error without changing selection."));
                BService.Control.Bind("Resources", "LoadResults", ReplacementRuntime.ResourceStatusLines,
                    ControlUpdatePolicy.WhenVisibleEverySecond,
                    new Translator("加载结果", "Loading Results"),
                    new Translator("当前资源状态，每秒更新。此处的显示值不控制资源包开关。", "Current resource state, updated every second. Displayed values do not change pack switches."));
                BindPulse("Resources", "RefreshResources", value => { if (value) ReplacementRuntime.Reload(); },
                    new Translator("刷新资源", "Refresh Resources"),
                    new Translator("与 Ctrl+T 相同：重新扫描并更新资源，取消旧的准备任务。", "Same as Ctrl+T: rescan resources and discard outdated preparation work."));
                BindPulse("Resources", "ExportResourceStatus", value =>
                {
                    if (!value) return;
                    try
                    {
                        ReplacementRuntime.ExportResourceStatus();
                        NoticeGUI.Show(new Translator("资源状态已导出至 logs/resource-status.json。",
                            "Resource status exported to logs/resource-status.json."), 5f);
                    }
                    catch (Exception ex)
                    {
                        BLog.Error("Resource status export failed.", ex);
                        NoticeGUI.Show(new Translator("资源状态导出失败，详见插件日志。", "Resource status export failed; see plugin log."), 5f);
                    }
                }, new Translator("导出资源状态", "Export Resource Status"),
                    new Translator("导出完整的当前状态和包/目标/文件错误，无需开启诊断。", "Export all current states and pack/target/file errors without enabling diagnostics."));
                BindPulse("Resources", "InspectMpcc", value =>
                {
                    if (!value) return;
                    try
                    {
                        MpccInspection.Export();
                        NoticeGUI.Show(new Translator("MPCC 报告已导出至 logs/mpcc-inspection.json。",
                            "MPCC report exported to logs/mpcc-inspection.json."), 5f);
                    }
                    catch (Exception ex)
                    {
                        BLog.Error("MPCC inspection failed.", ex);
                        NoticeGUI.Show(new Translator("MPCC 调查失败，详见插件日志。", "MPCC inspection failed; see plugin log."), 5f);
                    }
                }, new Translator("导出 MPCC 报告", "Export MPCC Report"),
                    new Translator("读取原游戏调色文件，列出部件和已加载图片的替换地址。不会应用调色。",
                        "Inspect original color presets and replacement addresses of loaded images without applying colors."));
            }
            catch (Exception ex) { BLog.Error("Failed to initialize resource controls.", ex); }
        }
    }
}
