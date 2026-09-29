using HarmonyLib;
using PixelLiner;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using XX;

namespace AICResourceKit.Patches.ReplaceTexture
{
    internal sealed class PxlSourceKey
    {
        internal readonly string AssetKey;
        internal readonly string TextKey;
        internal PxlSourceKey(string assetKey, string textKey) { AssetKey = assetKey; TextKey = textKey; }
    }

    internal static class ReplacementPxlSource
    {
        private static readonly ConditionalWeakTable<object, PxlSourceKey> sources = new ConditionalWeakTable<object, PxlSourceKey>();
        [ThreadStatic] internal static PxlSourceKey Current;

        internal static void Remember(object data, PxlSourceKey source)
        {
            if (data == null || source == null) return;
            sources.Remove(data);
            sources.Add(data, source);
        }

        internal static PxlSourceKey Find(object data) =>
            data != null && sources.TryGetValue(data, out var source) ? source : null;

        internal static void Loaded(MTI container, string key, object data)
        {
            string assetKey = MtiResourceAddress.ContainerKey(container.resources_path);
            if (assetKey != null && key != null) Remember(data, new PxlSourceKey(assetKey, key));
        }

        internal static void LoadedText(object bundle, string path, object text)
        {
            var source = Find(bundle);
            if (source == null || path == null) return;
            string prefix = "Assets/Editor/AssetBundlesSrc/" + source.AssetKey + "/";
            string key = path.StartsWith(prefix, StringComparison.Ordinal) ? path.Substring(prefix.Length) : path;
            Remember(text, new PxlSourceKey(source.AssetKey, key));
        }
    }

    [HarmonyPatch(typeof(MTI), nameof(MTI.LoadBytes), new[] { typeof(string) })]
    internal static class ReplacementPxlBytesPatch
    {
        [HarmonyPostfix]
        private static void Loaded(MTI __instance, string path, byte[] __result) =>
            ReplacementRuntime.GuardPxl(() => ReplacementPxlSource.Loaded(__instance, path, __result));
    }

    [HarmonyPatch]
    internal static class ReplacementPxlBundlePatch
    {
        internal static MethodBase TargetMethod() => AccessTools.Method(typeof(MTI), "initializeResource");

        [HarmonyPostfix]
        private static void Loaded(MTI __instance, UnityEngine.Object ___Bset) => ReplacementRuntime.GuardPxl(() =>
        {
            string key = MtiResourceAddress.ContainerKey(__instance.resources_path);
            if (key != null) ReplacementPxlSource.Remember(___Bset, new PxlSourceKey(key, null));
        });
    }

    [HarmonyPatch]
    internal static class ReplacementPxlTextPatch
    {
        // 不拦截 MTI.Load<T>：Mono 共享泛型代码，封闭类型补丁会影响 Font/Texture 等调用。
        internal static MethodBase TargetMethod() => AccessTools.TypeByName("UnityEngine.AssetBundle")
            .GetMethod("LoadAsset", new[] { typeof(string), typeof(Type) });

        [HarmonyPostfix]
        private static void Loaded(UnityEngine.Object __instance, string __0, UnityEngine.Object __result)
        {
            if (__result is TextAsset text)
                ReplacementRuntime.GuardPxl(() => ReplacementPxlSource.LoadedText(__instance, __0, text));
        }
    }

    [HarmonyPatch]
    internal static class ReplacementPxlTextContextPatch
    {
        internal static IEnumerable<MethodBase> TargetMethods() => typeof(PxlsLoader).GetMethods()
            .Where(method => method.Name == "loadCharacterASync"
                && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(TextAsset)))
            .Concat(new[] { typeof(PxlCharacter).GetMethod("loadASync", new[] { typeof(TextAsset) }) });

        [HarmonyPrefix]
        private static void Enter(object[] __args, out PxlSourceKey __state)
        {
            __state = ReplacementPxlSource.Current;
            ReplacementPxlSource.Current = ReplacementPxlSource.Find(__args.OfType<TextAsset>().FirstOrDefault());
        }

        [HarmonyFinalizer]
        private static void Leave(PxlSourceKey __state) => ReplacementPxlSource.Current = __state;
    }

    [HarmonyPatch(typeof(PxlCharacter), "loadASync", new[] { typeof(byte[]), typeof(bool) })]
    internal static class ReplacementPxlCharacterSourcePatch
    {
        [HarmonyPrefix]
        private static void Loading(PxlCharacter __instance, byte[] bytes) => ReplacementRuntime.GuardPxl(() =>
            ReplacementRuntime.RegisterPxlSource(__instance, ReplacementPxlSource.Find(bytes) ?? ReplacementPxlSource.Current));
    }
}
