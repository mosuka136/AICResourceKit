using AICResourceKit.Contracts;
using HarmonyLib;
using Spine.Unity;
using System;
using System.Reflection;
using UnityEngine;
using XX;

namespace AICResourceKit.Patches.ReplaceTexture
{
    [HarmonyPatch]
    internal static class ReplacementSpineAssetsSourcePatch
    {
        internal static MethodBase TargetMethod() => AccessTools.Method(typeof(SpineViewer), "prepareAtlasAssetsS", new[]
        {
            typeof(string), typeof(string), typeof(MTISpine), typeof(SpineAtlasAsset).MakeByRefType(), typeof(SkeletonDataAsset).MakeByRefType()
        });

        [HarmonyPrefix]
        private static void Enter(string atlas_key, string json_key, MTISpine Mti, out SpineResourceAddress __state)
        {
            SpineResourceAddress address = null;
            ReplacementRuntime.GuardSpineViewer(() => address = ReplacementRuntime.ViewerSpineAddress(atlas_key, json_key, Mti));
            __state = address;
        }

        [HarmonyPostfix]
        private static void Loaded(SpineResourceAddress __state, SpineAtlasAsset SpAtlasAsset, SkeletonDataAsset SpDataAsset) =>
            ReplacementRuntime.GuardSpineViewer(() => ReplacementRuntime.RememberSpineAssets(__state, SpAtlasAsset, SpDataAsset));
    }

    [HarmonyPatch(typeof(SpineViewer), "prepareAtlasAssets")]
    internal static class ReplacementOrdinarySpinePreparedPatch
    {
        [HarmonyPostfix]
        private static void Loaded(SpineViewer __instance, ref SpineAtlasAsset SpAtlasAsset, ref SkeletonDataAsset SpDataAsset)
        {
            var atlas = SpAtlasAsset; var data = SpDataAsset;
            ReplacementRuntime.GuardSpineViewer(() => ReplacementRuntime.ObserveSpineViewer(__instance, atlas, data));
            // 参数通常直接指向查看器字段；这里也兼容使用局部变量调用的子类。
            if (ReplacementRuntime.OrdinaryAssets(__instance, out var replacementAtlas, out var replacementData))
            { SpAtlasAsset = replacementAtlas; SpDataAsset = replacementData; }
        }
    }

    [HarmonyPatch(typeof(SpineViewer), nameof(SpineViewer.attachPreloadAssets))]
    internal static class ReplacementOrdinarySpinePreloadPatch
    {
        [HarmonyPostfix]
        private static void Loaded(SpineViewer __instance, SpineAtlasAsset _SpAtlasAsset, SkeletonDataAsset _SpDataAsset, Texture _Tx, Material _Mtr) =>
            ReplacementRuntime.GuardSpineViewer(() => ReplacementRuntime.ObserveSpineViewer(__instance, _SpAtlasAsset, _SpDataAsset, _Tx, _Mtr));
    }

    [HarmonyPatch(typeof(SpineViewer), nameof(SpineViewer.prepareMaterial))]
    internal static class ReplacementOrdinarySpineMaterialPatch
    {
        [HarmonyPrefix]
        private static bool Loading(SpineViewer __instance, Material _Mtr, ref Material __result)
        {
            Material result = null;
            bool replaced = false;
            ReplacementRuntime.GuardSpineViewer(() => replaced = ReplacementRuntime.PrepareOrdinaryMaterial(__instance, _Mtr, out result));
            if (replaced) __result = result;
            return !replaced;
        }
    }

    [HarmonyPatch(typeof(SpineViewer), nameof(SpineViewer.prepareTexture))]
    internal static class ReplacementOrdinarySpineTexturePatch
    {
        [HarmonyPrefix]
        private static bool Loading(SpineViewer __instance, ref Texture __result)
        {
            if (!ReplacementRuntime.OrdinaryTexture(__instance, out var texture)) return true;
            __result = texture; return false;
        }
    }

    [HarmonyPatch(typeof(SpineViewer), nameof(SpineViewer.fineAtlasMaterial))]
    internal static class ReplacementOrdinarySpineFinePatch
    {
        [HarmonyPrefix]
        private static bool Updating(SpineViewer __instance)
        {
            bool handled = false;
            ReplacementRuntime.GuardSpineViewer(() => handled = ReplacementRuntime.FineOrdinaryMaterial(__instance));
            return !handled;
        }
    }

    [HarmonyPatch(typeof(SpineViewer), nameof(SpineViewer.copyAnimationFrom))]
    internal static class ReplacementOrdinarySpineCopyPatch
    {
        [HarmonyPostfix]
        private static void Copied(SpineViewer __instance, SpineViewer Spw) =>
            ReplacementRuntime.GuardSpineViewer(() => ReplacementRuntime.CopyOrdinaryViewer(__instance, Spw));
    }

    [HarmonyPatch(typeof(SpineViewer), nameof(SpineViewer.reloadAnimKey))]
    internal static class ReplacementOrdinarySpineReloadPatch
    {
        [HarmonyPrefix]
        private static void Changing(SpineViewer __instance, string _key)
        {
            if (__instance.key != _key) ReplacementRuntime.GuardSpineViewer(() => ReplacementRuntime.ReleaseOrdinaryViewer(__instance));
        }
    }

    [HarmonyPatch(typeof(SpineViewer), nameof(SpineViewer.destruct))]
    internal static class ReplacementOrdinarySpineReleasePatch
    {
        [HarmonyPrefix]
        private static void Releasing(SpineViewer __instance) =>
            ReplacementRuntime.GuardSpineViewer(() => ReplacementRuntime.ReleaseOrdinaryViewer(__instance));
    }
}
