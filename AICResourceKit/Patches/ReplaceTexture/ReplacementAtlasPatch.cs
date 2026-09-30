using HarmonyLib;
using Spine;
using Spine.Unity;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AICResourceKit.Patches.ReplaceTexture
{
    [HarmonyPatch(typeof(Resources), nameof(Resources.Load), new[] { typeof(string), typeof(System.Type) })]
    internal static class ReplacementAtlasResourcesSourcePatch
    {
        [HarmonyPostfix]
        private static void Loaded(string __0, UnityEngine.Object __result)
        {
            if (__result is TextAsset text) ReplacementRuntime.GuardSpineViewer(() =>
                ReplacementRuntime.RememberAtlasResourceText(text, __0));
        }
    }

    [HarmonyPatch(typeof(SpineAtlasAsset), nameof(SpineAtlasAsset.GetAtlas))]
    internal static class ReplacementAtlasLoadedPatch
    {
        [HarmonyPostfix]
        private static void Loaded(SpineAtlasAsset __instance, Atlas __result) =>
            ReplacementRuntime.GuardSpineViewer(() => ReplacementRuntime.RegisterAtlas(__instance, __result));
    }

    [HarmonyPatch(typeof(SpineAtlasAsset), nameof(SpineAtlasAsset.Clear))]
    internal static class ReplacementAtlasClearPatch
    {
        [HarmonyPrefix]
        private static void Clearing(SpineAtlasAsset __instance) =>
            ReplacementRuntime.GuardSpineViewer(() => ReplacementRuntime.ReleaseAtlas(__instance));
    }

    [HarmonyPatch]
    internal static class ReplacementPictureAtlasPatch
    {
        private static readonly System.Type loader = AccessTools.TypeByName("nel.fatal.SpvLoader");
        private static readonly FieldInfo atlas = AccessTools.Field(loader, "SpAtlasAsset");
        private static readonly FieldInfo texture = AccessTools.Field(loader, "Tx");

        internal static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(loader, "initTexture");
            yield return AccessTools.Method(loader, "GetImage");
        }

        [HarmonyPostfix]
        private static void Loaded(object __instance, MethodBase __originalMethod, object[] __args) => ReplacementRuntime.GuardSpineViewer(() =>
        {
            var asset = atlas.GetValue(__instance) as SpineAtlasAsset;
            ReplacementRuntime.RegisterAtlas(asset, null, texture.GetValue(__instance) as Texture2D);
            if (__originalMethod.Name == "GetImage" && __args.Length > 0)
                ReplacementRuntime.DescribeAtlasRegion(asset, __args[0] as string);
        });
    }
}
