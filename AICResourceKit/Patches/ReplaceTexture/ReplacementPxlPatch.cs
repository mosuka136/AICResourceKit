using HarmonyLib;
using PixelLiner;
using PixelLiner.PixelLinerLib;
using UnityEngine;

namespace AICResourceKit.Patches.ReplaceTexture
{
    [HarmonyPatch(typeof(PxlCharacter), nameof(PxlCharacter.createFromOther))]
    internal static class ReplacementPxlSharedImagesPatch
    {
        [HarmonyPostfix]
        private static void Shared(PxlCharacter __instance) =>
            ReplacementRuntime.GuardPxl(() => ReplacementRuntime.SharePxlImages(__instance));
    }

    [HarmonyPatch(typeof(PxlImage), "readFromBytes", new[] { typeof(ByteReader) })]
    internal static class ReplacementPxlImagePatch
    {
        [HarmonyPostfix]
        private static void Decoded(PxlImage __instance) =>
            ReplacementRuntime.GuardPxl(() => ReplacementRuntime.RegisterPxlImage(__instance));
    }

    [HarmonyPatch(typeof(PxlsImgAtlas), "readFromBytesStack")]
    internal static class ReplacementPxlAtlasPatch
    {
        [HarmonyPostfix]
        private static void Decoded(PxlsImgAtlas[] AStackedLoding)
        {
            if (AStackedLoding?.Length > 0) ReplacementRuntime.GuardPxl(() =>
                ReplacementRuntime.RegisterPxlAtlas(AStackedLoding[AStackedLoding.Length - 1], AStackedLoding.Length - 1));
        }
    }

    [HarmonyPatch(typeof(PxlCharacter), nameof(PxlCharacter.ReplaceExternalPng), new[] { typeof(Texture[]), typeof(bool) })]
    internal static class ReplacementPxlExternalPatch
    {
        [HarmonyPostfix]
        private static void Assigned(PxlCharacter __instance) =>
            ReplacementRuntime.GuardPxl(() => ReplacementRuntime.RegisterPxlExternal(__instance));
    }

    [HarmonyPatch(typeof(PxlsTexture), nameof(PxlsTexture.assignTexture), new[] { typeof(Texture), typeof(bool) })]
    internal static class ReplacementPxlTexturePatch
    {
        [HarmonyPostfix]
        private static void Assigned(PxlsTexture __instance) =>
            ReplacementRuntime.GuardPxl(() => ReplacementRuntime.RebindPxlTexture(__instance));
    }

    [HarmonyPatch(typeof(PxlsTexture), nameof(PxlsTexture.Unload), new[] { typeof(bool) })]
    internal static class ReplacementPxlUnloadPatch
    {
        [HarmonyPrefix]
        private static void Unloading(PxlsTexture __instance, bool only_external)
        {
            if (!only_external || __instance.texture_path != null)
                ReplacementRuntime.GuardPxl(() => ReplacementRuntime.UnbindPxlTexture(__instance));
        }
    }

    [HarmonyPatch(typeof(PxlCharacter), nameof(PxlCharacter.Destroy), new[] { typeof(bool) })]
    internal static class ReplacementPxlReleasePatch
    {
        [HarmonyPrefix]
        private static void Releasing(PxlCharacter __instance) =>
            ReplacementRuntime.GuardPxl(() => ReplacementRuntime.ReleasePxl(__instance));
    }
}
