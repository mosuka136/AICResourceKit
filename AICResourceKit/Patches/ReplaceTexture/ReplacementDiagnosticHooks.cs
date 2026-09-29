using HarmonyLib;
using nel;
using PixelLiner;
using PixelLiner.PixelLinerLib;
using Spine.Unity;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using XX;
using XX.mobpxl;

namespace AICResourceKit.Patches.ReplaceTexture
{
    internal sealed class ReplacementDiagnosticHook
    {
        internal readonly Type Type;
        internal readonly string Method;
        internal readonly Type[] Parameters;
        internal readonly string Callback;
        internal readonly bool Prefix;

        internal ReplacementDiagnosticHook(Type type, string method, Type[] parameters, string callback, bool prefix = false)
        {
            Type = type; Method = method; Parameters = parameters; Callback = callback; Prefix = prefix;
        }

        internal MethodInfo Resolve() => Type?.GetMethod(Method,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null, Parameters, null);
        internal string Signature => (Type?.FullName ?? "<unavailable>") + "." + Method
            + "(" + string.Join(",", Parameters.Select(type => type.Name)) + ")";
    }

    internal static class ReplacementDiagnosticHooks
    {
        private sealed class MovieState { internal string State; }
        private static ConditionalWeakTable<object, MovieState> movies = new ConditionalWeakTable<object, MovieState>();

        internal static IEnumerable<ReplacementDiagnosticHook> Definitions()
        {
            yield return new ReplacementDiagnosticHook(typeof(MTI), "LoadImage", new[] { typeof(string) }, nameof(MtiImage));
            yield return new ReplacementDiagnosticHook(typeof(MTI), "Dispose", Type.EmptyTypes, nameof(MtiDisposed));
            yield return new ReplacementDiagnosticHook(typeof(SpineViewer), "prepareAtlasAssetsS", new[]
            {
                typeof(string), typeof(string), typeof(MTISpine),
                typeof(SpineAtlasAsset).MakeByRefType(), typeof(SkeletonDataAsset).MakeByRefType()
            }, nameof(SpinePrepared));
            yield return new ReplacementDiagnosticHook(typeof(MTRX), "loadMtiPxc", new[]
            {
                typeof(MTIOneImage).MakeByRefType(), typeof(string), typeof(string), typeof(string),
                typeof(bool), typeof(bool), typeof(bool)
            }, nameof(PxlLoaded));
            yield return new ReplacementDiagnosticHook(typeof(PxlImage), "readFromBytes", new[] { typeof(ByteReader) }, nameof(PxlDecoded));
            yield return new ReplacementDiagnosticHook(typeof(PxlsImgAtlas), "readFromBytesStack",
                new[] { typeof(PxlsImgAtlas[]).MakeByRefType(), typeof(ByteReader), typeof(PxlCharacter) }, nameof(PxlAtlasDecoded));
            yield return new ReplacementDiagnosticHook(typeof(PxlCharacter), "ReplaceExternalPng",
                new[] { typeof(Texture[]), typeof(bool) }, nameof(PxlPages));
            yield return new ReplacementDiagnosticHook(typeof(PxlCharacter), "Destroy", new[] { typeof(bool) }, nameof(PxlReleased));
            yield return new ReplacementDiagnosticHook(typeof(PR).Assembly.GetType("nel.fatal.SpvLoader"),
                "GetImage", new[] { typeof(string) }, nameof(AtlasRegion));
            yield return new ReplacementDiagnosticHook(typeof(MobPCCContainer), "readFromBytesFromFile",
                new[] { typeof(ByteArray), typeof(SkltImage), typeof(PxlCharacter) }, nameof(MpccRead));
            yield return new ReplacementDiagnosticHook(typeof(FillBlockMovie), "initMovie",
                new[] { typeof(string), typeof(int), typeof(int), typeof(MTI) }, nameof(Movie));
            yield return new ReplacementDiagnosticHook(typeof(FillBlockMovie), "runIRD", new[] { typeof(float) }, nameof(Movie));
            yield return new ReplacementDiagnosticHook(typeof(FillBlockMovie), "closeMovie", Type.EmptyTypes, nameof(MovieReleased), true);
            yield return new ReplacementDiagnosticHook(typeof(M2UnstbMovie), "RenderToCam",
                new[] { typeof(object), typeof(ProjectionContainer), typeof(Camera) }, nameof(Movie));
            yield return new ReplacementDiagnosticHook(typeof(M2UnstbMovie), "Dispose", Type.EmptyTypes, nameof(MovieReleased), true);
        }

        internal static void Install(Harmony harmony, IList<object> report)
        {
            foreach (var hook in Definitions())
            {
                var record = new Dictionary<string, object> { ["signature"] = hook.Signature };
                report.Add(record);
                try
                {
                    var method = hook.Resolve() ?? throw new MissingMethodException(hook.Signature);
                    var callback = new HarmonyMethod(typeof(ReplacementDiagnosticHooks), hook.Callback);
                    if (hook.Prefix) harmony.Patch(method, prefix: callback);
                    else harmony.Patch(method, postfix: callback);
                    record["status"] = "installed";
                }
                catch (Exception ex)
                {
                    record["status"] = "unavailable";
                    record["reason"] = ReplacementDiagnosticRuntime.Sanitize(ex.GetType().Name + ": " + ex.Message);
                }
            }
            foreach (var method in ExistingEntries())
            {
                var patches = method == null ? null : Harmony.GetPatchInfo(method);
                report.Add(new Dictionary<string, object>
                {
                    ["signature"] = method?.DeclaringType.FullName + "." + method,
                    ["status"] = patches?.Owners.Contains(PatchInfo.HarmonyPluginId) == true ? "installed" : "unavailable",
                    ["kind"] = "existing-replacement-entry"
                });
            }
        }

        internal static IEnumerable<MethodBase> ExistingEntries()
        {
            yield return typeof(MTI).GetMethod("LoadContainerOneImage", new[] { typeof(string), typeof(string), typeof(string) });
            yield return typeof(Resources).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == "Load" && !method.IsGenericMethod && method.GetParameters().Length == 1);
            yield return typeof(Resources).GetMethod("Load", new[] { typeof(string), typeof(Type) });
            yield return typeof(BetobetoManager.SvTexture).GetMethod("prepareAtlasAssets",
                new[] { typeof(SpineAtlasAsset).MakeByRefType(), typeof(SkeletonDataAsset).MakeByRefType(), typeof(Material[]), typeof(string) });
        }

        internal static void ClearObservations() => movies = new ConditionalWeakTable<object, MovieState>();

        private static void MtiImage(MTI __instance, string path, MImage __result)
        {
            ReplacementDiagnosticRuntime.Guard(() => ReplacementDiagnosticRuntime.Record(
                new ReplacementDiagnosticTarget("mti-image", ReplacementDiagnosticRuntime.MtiKey(__instance), path, "Texture"),
                "entry-hit", "XX.MTI.LoadImage(string)", __result?.Tx == null ? "image-unavailable" : "image-returned",
                details: new Dictionary<string, object> { ["containerType"] = __instance.GetType().FullName }));
        }

        private static void MtiDisposed(MTI __instance)
        {
            ReplacementDiagnosticRuntime.Guard(() => ReplacementDiagnosticRuntime.Record(
                new ReplacementDiagnosticTarget("mti-container", ReplacementDiagnosticRuntime.MtiKey(__instance), null, "MTI"),
                "released", "XX.MTI.Dispose()", "container-disposed"));
        }

        private static void SpinePrepared(string atlas_key, string json_key, MTISpine Mti,
            SpineAtlasAsset SpAtlasAsset, SkeletonDataAsset SpDataAsset)
        {
            ReplacementDiagnosticRuntime.Guard(() => ReplacementDiagnosticRuntime.Record(
                new ReplacementDiagnosticTarget("spine-assets", ReplacementDiagnosticRuntime.MtiKey(Mti), json_key, "SkeletonDataAsset"),
                "entry-hit", "XX.SpineViewer.prepareAtlasAssetsS(string,string,MTISpine,out,out)",
                SpAtlasAsset != null && SpDataAsset != null ? "assets-returned" : "assets-unavailable",
                details: new Dictionary<string, object> { ["atlasKey"] = atlas_key, ["loader"] = Mti == null ? "resources" : "mti" }));
        }

        private static void PxlLoaded(PxlCharacter __result, string pxl_name, string pxls_path,
            string image_mti_load_key, bool load_external, bool load_external_async)
        {
            ReplacementDiagnosticRuntime.Guard(() => ReplacementDiagnosticRuntime.Record(
                new ReplacementDiagnosticTarget("pxl", pxls_path, pxl_name, "PxlCharacter"),
                "entry-hit", "XX.MTRX.loadMtiPxc(out,string,string,string,bool,bool,bool)",
                __result == null ? "character-unavailable" : "character-returned",
                details: new Dictionary<string, object>
                {
                    ["loadExternal"] = load_external, ["asyncExternal"] = load_external_async,
                    ["loadKey"] = image_mti_load_key, ["decodingMayBePending"] = true
                }));
        }

        private static void PxlDecoded(PxlImage __instance)
        {
            ReplacementDiagnosticRuntime.Guard(() =>
            {
                string id = __instance.id.ToString(CultureInfo.InvariantCulture) + ":"
                    + __instance.id2.ToString("R", CultureInfo.InvariantCulture);
                foreach (string role in new[] { "I", "P" })
                {
                    var image = AccessTools.Field(typeof(PxlImage), role).GetValue(__instance) as Texture;
                    if (image == null) continue;
                    ReplacementDiagnosticRuntime.Record(new ReplacementDiagnosticTarget("pxl-embedded",
                        __instance.pChar?.external_png_header, id + "/" + role, "Texture2D"),
                        "entry-hit", "PixelLiner.PxlImage.readFromBytes(ByteReader)", "embedded-image-decoded",
                        details: new Dictionary<string, object>
                        {
                            ["characterTitle"] = __instance.pChar?.title, ["imageId"] = id, ["role"] = role,
                            ["width"] = image.width, ["height"] = image.height
                        });
                }
            });
        }

        private static void PxlPages(PxlCharacter __instance, Texture[] ATx)
        {
            ReplacementDiagnosticRuntime.Guard(() =>
            {
                if (ATx == null) return;
                for (int i = 0; i < ATx.Length; i++)
                    ReplacementDiagnosticRuntime.Record(new ReplacementDiagnosticTarget("pxl-external-page",
                        __instance.external_png_header, i.ToString(CultureInfo.InvariantCulture), "Texture"),
                        "entry-hit", "PixelLiner.PxlCharacter.ReplaceExternalPng(Texture[],bool)",
                        ATx[i] == null ? "page-unavailable" : "page-assigned",
                        details: new Dictionary<string, object> { ["characterTitle"] = __instance.title, ["pageIndex"] = i });
            });
        }

        private static void PxlAtlasDecoded(PxlsImgAtlas[] AStackedLoding)
        {
            ReplacementDiagnosticRuntime.Guard(() =>
            {
                if (AStackedLoding == null || AStackedLoding.Length == 0) return;
                int index = AStackedLoding.Length - 1;
                var atlas = AStackedLoding[index];
                var regions = AccessTools.Field(typeof(PxlsImgAtlas), "Apos").GetValue(atlas) as PxlsImgAtlasUv[];
                ReplacementDiagnosticRuntime.Record(new ReplacementDiagnosticTarget("pxl-packed-page",
                    atlas.pChar?.external_png_header, index.ToString(CultureInfo.InvariantCulture), "PxlsImgAtlas"),
                    "entry-hit", "PixelLiner.PxlsImgAtlas.readFromBytesStack(ref,ByteReader,PxlCharacter)",
                    atlas.image_index < 0 ? "embedded-page-decoded" : "external-page-described",
                    details: new Dictionary<string, object>
                    {
                        ["characterTitle"] = atlas.pChar?.title, ["pageOrdinal"] = index,
                        ["externalPageIndex"] = atlas.image_index, ["imageType"] = atlas.img_type,
                        ["regionCount"] = regions?.Length ?? 0, ["sampleTruncated"] = (regions?.Length ?? 0) > 16,
                        ["imageIdSample"] = regions?.Take(16).Select(region => (object)(region.key.id.ToString(CultureInfo.InvariantCulture)
                            + ":" + region.key.id2.ToString("R", CultureInfo.InvariantCulture))).ToArray()
                    });
            });
        }

        private static void PxlReleased(PxlCharacter __instance, bool no_dispose_image)
        {
            ReplacementDiagnosticRuntime.Guard(() => ReplacementDiagnosticRuntime.Record(
                new ReplacementDiagnosticTarget("pxl", __instance.external_png_header, __instance.title, "PxlCharacter"),
                "released", "PixelLiner.PxlCharacter.Destroy(bool)", no_dispose_image ? "character-closed-images-retained" : "character-images-released"));
        }

        private static void AtlasRegion(object __instance, string ikey, Spine.AtlasRegion __result)
        {
            ReplacementDiagnosticRuntime.Guard(() =>
            {
                var mti = AccessTools.Field(__instance.GetType(), "MtiText").GetValue(__instance) as MTI;
                ReplacementDiagnosticRuntime.Record(new ReplacementDiagnosticTarget("atlas-region",
                    ReplacementDiagnosticRuntime.MtiKey(mti), ikey, "AtlasRegion"), "entry-hit",
                    "nel.fatal.SpvLoader.GetImage(string)", __result == null ? "region-unavailable" : "region-returned",
                    details: new Dictionary<string, object>
                    {
                        ["jsonKey"] = AccessTools.Field(__instance.GetType(), "key").GetValue(__instance),
                        ["page"] = __result?.page?.name, ["degrees"] = __result?.degrees
                    });
            });
        }

        private static void MpccRead(MobPCCContainer __instance, PxlCharacter Pcr)
        {
            ReplacementDiagnosticRuntime.Guard(() => ReplacementDiagnosticRuntime.Record(
                new ReplacementDiagnosticTarget("mpcc-decoded", __instance.chr_name, __instance.name, "SkltPalette"),
                "entry-hit", "XX.mobpxl.MobPCCContainer.readFromBytesFromFile(ByteArray,SkltImage,PxlCharacter)", "palette-read",
                "The decoder receives bytes, not a file path; filename attribution remains unverified.",
                new Dictionary<string, object> { ["pxlSource"] = Pcr?.external_png_header, ["imageCount"] = __instance.target_image_count }));
        }

        private static void Movie(object __instance, MethodBase __originalMethod)
        {
            ReplacementDiagnosticRuntime.Guard(() => ObserveMovie(__instance, __originalMethod, false));
        }

        private static void MovieReleased(object __instance, MethodBase __originalMethod)
        {
            ReplacementDiagnosticRuntime.Guard(() => ObserveMovie(__instance, __originalMethod, true));
        }

        private static void ObserveMovie(object instance, MethodBase method, bool released)
        {
            Type type = instance is FillBlockMovie ? typeof(FillBlockMovie) : typeof(M2UnstbMovie);
            var mti = AccessTools.Field(type, type == typeof(FillBlockMovie) ? "MiMov" : "MI").GetValue(instance) as MTI;
            string key = AccessTools.Field(type, "movie_filename").GetValue(instance) as string;
            if (mti == null || string.IsNullOrEmpty(key)) return;
            string state = AccessTools.Field(type, "state").GetValue(instance).ToString();
            var player = AccessTools.Field(type, "Vdp").GetValue(instance);
            bool hasClip = player != null && AccessTools.Property(player.GetType(), "clip").GetValue(player, null) is UnityEngine.Object clip
                && clip != null;
            string token = PortraitJson.Serialize(new object[] { ReplacementDiagnosticRuntime.MtiKey(mti), key, state, hasClip, released });
            var previous = movies.GetValue(instance, _ => new MovieState());
            if (previous.State == token) return;
            previous.State = token;
            ReplacementDiagnosticRuntime.Record(new ReplacementDiagnosticTarget("video-clip",
                ReplacementDiagnosticRuntime.MtiKey(mti), key, "VideoClip"), released ? "released" : "entry-hit",
                method.DeclaringType.FullName + "." + method.Name, released ? "close-requested" : "player-state",
                details: new Dictionary<string, object> { ["state"] = state, ["hasClip"] = hasClip });
        }
    }
}
