using AICResourceKit.BLogSpace;
using AICResourceKit.BPatchGUI;
using AICResourceKit.Patches.ReplaceTexture;
using System;
using UnityModBase.HTranslatorSpace;

namespace AICResourceKit.BControlManager
{
    internal static partial class ControlManager
    {
        private static void InitializeResources()
        {
            try
            {
                BService.Control.CreateTable("Resources", new Translator("资源调查", "Resources"));
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
