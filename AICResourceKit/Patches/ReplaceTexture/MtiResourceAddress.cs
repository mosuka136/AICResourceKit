using System;
using XX;

namespace AICResourceKit.Patches.ReplaceTexture
{
    internal static class MtiResourceAddress
    {
        private const string Prefix = "Assets/Editor/AssetBundlesSrc/";

        internal static string ContainerKey(string resourcesPath)
        {
            if (resourcesPath == null || resourcesPath.Length <= Prefix.Length + 1
                || !resourcesPath.StartsWith(Prefix, StringComparison.Ordinal)
                || !resourcesPath.EndsWith("/", StringComparison.Ordinal)) return null;
            return resourcesPath.Substring(Prefix.Length, resourcesPath.Length - Prefix.Length - 1);
        }

        // 单图容器的 LoadImage 是其初始化步骤，仍由原入口按调用方的 null/空键语义管理。
        internal static bool UsesDirectImageEntry(MTI container) => container != null && !(container is MTIOneImage);
    }
}
