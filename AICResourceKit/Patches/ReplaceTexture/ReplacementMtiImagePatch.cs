using AICResourceKit.BLogSpace;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using XX;

namespace AICResourceKit.Patches.ReplaceTexture
{
    [HarmonyPatch(typeof(MTI), nameof(MTI.LoadImage), new[] { typeof(string) })]
    internal static class ReplacementMtiImagePatch
    {
        [HarmonyPostfix]
        private static void Loaded(MTI __instance, string path, MImage __result)
        {
            try { ReplacementRuntime.RegisterMtiImage(__instance, path, __result); }
            catch (Exception ex) { BLog.Error("Failed to register a direct MTI image: " + path, ex); }
        }
    }

    [HarmonyPatch]
    internal static class ReplacementMtiReleasePatch
    {
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            yield return typeof(MTI).GetMethod(nameof(MTI.Dispose), Type.EmptyTypes);
            yield return typeof(MTI).GetMethod(nameof(MTI.UnloadAll), Type.EmptyTypes);
        }

        // MTIOneImage.Dispose 先调用基类；在游戏卸载纹理前取消准备、恢复绑定并释放自建对象。
        [HarmonyPrefix]
        private static void Releasing(MTI __instance)
        {
            try { ReplacementRuntime.ReleaseMti(__instance); }
            catch (Exception ex) { BLog.Error("Failed to release MTI replacement images.", ex); }
        }
    }
}
