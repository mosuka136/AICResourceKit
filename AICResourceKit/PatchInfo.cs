using BepInEx;
using System.IO;
using UnityModBase.HTranslatorSpace;

namespace AICResourceKit
{
    /// <summary>
    /// 定义插件在 BepInEx、Harmony 和文件系统中的固定标识与目录约定。
    /// 该类型只保存启动阶段和资源加载阶段共享的常量/路径，不负责创建目录或验证文件存在性。
    /// </summary>
    public class PatchInfo
    {
        public static readonly Translator UserName = new Translator("AIC资源替换", "AICResourceKit");

        public const string BepInPluginId = "com.buele.aicresourcekit";
        public const string BepInPluginVersion = Contracts.ResourceCapabilities.PluginVersion;

        public const string HarmonyPluginId = "com.buele.aicresourcekit";
        public const string HarmonyPluginVersion = BepInPluginVersion;

        public static readonly string PluginPath = Path.Combine(Paths.PluginPath, nameof(AICResourceKit));

        public static readonly string ConfigFilePath = Path.Combine(PluginPath, $"{nameof(AICResourceKit)}.cfg");

        public static readonly string LoggerPath = Path.Combine(PluginPath, "logs");
        public const string LoggerName = "AICResourceKit.log";

        public static readonly string ReplaceImagePath = Path.Combine(PluginPath, "ReplaceTexture");
        public static readonly string ReplaceSensitiveImagePath = Path.Combine(ReplaceImagePath, "Sensitive");
    }
}
