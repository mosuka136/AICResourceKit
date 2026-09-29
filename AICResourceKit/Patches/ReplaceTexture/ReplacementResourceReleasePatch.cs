using AICResourceKit.BLogSpace;
using HarmonyLib;
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AICResourceKit.Patches.ReplaceTexture
{
    [HarmonyPatch(typeof(Resources), nameof(Resources.UnloadAsset), new[] { typeof(Object) })]
    internal static class ReplacementResourceReleasePatch
    {
        [HarmonyPrefix]
        private static bool Releasing(ref Object __0)
        {
            try
            {
                __0 = ReplacementRuntime.ReleaseResource(__0);
                return __0 != null;
            }
            catch (Exception ex)
            {
                BLog.Error("Failed to release a Resources replacement.", ex);
                return true;
            }
        }
    }
}
