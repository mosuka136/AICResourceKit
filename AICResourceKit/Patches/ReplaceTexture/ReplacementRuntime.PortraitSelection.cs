using AICResourceKit.BLogSpace;
using HarmonyLib;
using nel;
using Spine;
using Spine.Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AICResourceKit.Patches.ReplaceTexture
{
    internal static partial class ReplacementRuntime
    {
        private sealed class PortraitContext
        {
            internal string Pose, Animation;
            internal uint State;
            internal bool Collecting, PreservePlayback;
            internal string[] Animations = new string[0], Skins = new string[0];
            internal PortraitPxlImage Image;
            internal UIPictureBodySpine Body;

            internal bool Matches(ReplacementTarget target) => target.PortraitSelection == null
                || (!Collecting && target.PortraitSelection.Matches(Pose, State, Animation));
        }

        private sealed class PortraitPxlImage
        {
            internal Texture Original;
            internal Texture2D Image;
            internal ReplacementTarget Target;
            internal bool Failed;
        }

        private static readonly ConditionalWeakTable<UIPictureBodyData, PortraitContext> portraitContexts =
            new ConditionalWeakTable<UIPictureBodyData, PortraitContext>();
        private static readonly ConditionalWeakTable<SpineViewerNel, PortraitContext> portraitSpineContexts =
            new ConditionalWeakTable<SpineViewerNel, PortraitContext>();
        private static readonly List<WeakReference> portraitBodies = new List<WeakReference>();
        private static readonly List<PortraitPxlImage> portraitPxlImages = new List<PortraitPxlImage>();

        internal static void BeginPortrait(UIPictureBodyData body, UIPictureBase.EMSTATE state)
        {
            if (!initialized || body == null) return;
            var context = portraitContexts.GetValue(body, _ => new PortraitContext());
            context.Pose = body.Emot?.emot_id.ToString();
            context.State = (uint)state;
            context.Image = null;
            if (!portraitBodies.Any(entry => ReferenceEquals(entry.Target, body))) portraitBodies.Add(new WeakReference(body));
        }

        // 先让原版选择完整的轨道/皮肤，再为该状态准备候选；破衣等排除状态不会访问候选素材。
        internal static void BeginPortraitSpine(UIPictureBodySpine body, UIPictureBase.EMSTATE state)
        {
            if (!initialized || body == null) return;
            BeginPortrait(body, state);
            var viewer = body.getViewer();
            var texture = viewer?.getSvTexture();
            if (texture == null) return;
            string key = viewer.replace_json_key ?? texture.MtiText.default_json_key;
            string identity = SpineIdentity(texture.key, key);
            bool conditional = IsActivatingResourcePreview(body)
                || selection.Layers(identity).Any(layer => layer.PortraitSelection != null)
                || (spineStates.TryGetValue(texture, out var previous) && previous.Conditional);
            var context = portraitContexts.GetValue(body, _ => new PortraitContext());
            context.Body = body;
            context.Collecting = true;
            context.Animation = null;
            portraitSpineContexts.Remove(viewer);
            portraitSpineContexts.Add(viewer, context);
            Register(viewer);
            if (!conditional) return;
            context.PreservePlayback = true;
            if (spineStates.TryGetValue(texture, out var resource))
            {
                CancelSpinePreparation(resource);
                if (resource.Preview != null) RemoveResourcePreview(texture, resource);
                if (resource.Current != null) InstallSpine(texture, resource, key, null);
                resource.Attempt = -1;
            }
        }

        internal static void CompletePortraitSpine(UIPictureBodySpine body)
        {
            var viewer = body?.getViewer();
            if (viewer == null || !portraitSpineContexts.TryGetValue(viewer, out var context) || !context.Collecting) return;
            context.Animation = viewer.getBaseAnimName();
            context.Skins = CaptureSkinNames(viewer.GetSkeleton()?.SkinList);
            context.Animations = animator.GetValue(viewer) is SkeletonAnimation animation
                ? SpinePlayback.AnimationNames(animation.state) : new string[0];
            context.Collecting = false;
            if (CompleteResourcePreview(body, context)) return;
            if (spineStates.TryGetValue(viewer.getSvTexture(), out var resource))
            {
                CancelSpinePreparation(resource);
                resource.Attempt = -1;
            }
        }

        private static PortraitContext PortraitContextFor(BetobetoManager.SvTexture texture, SpineViewerNel viewer)
        {
            if (viewer != null && portraitSpineContexts.TryGetValue(viewer, out var context)) return context;
            foreach (var candidate in LiveViewers().Where(item => item.enabled && item.getSvTexture() == texture))
                if (portraitSpineContexts.TryGetValue(candidate, out context)) return context;
            return null;
        }

        internal static void ValidatePortraitData(SkeletonData data, IEnumerable<string> animations, IEnumerable<string> skins)
        {
            foreach (string name in animations)
                if (data.FindAnimation(name) == null) throw new InvalidDataException("Selected portrait state needs animation: " + name);
            foreach (string name in skins)
                if (data.FindSkin(name) == null) throw new InvalidDataException("Selected portrait state needs skin: " + name);
        }

        private static bool ReplaySelectedPortrait(SpineViewerNel viewer)
        {
            if (!portraitSpineContexts.TryGetValue(viewer, out var context) || context.Collecting || !context.PreservePlayback
                || !(animator.GetValue(viewer) is SkeletonAnimation animation) || animation.state == null) return false;
            var previous = animation.skeletonDataAsset;
            viewer.prepareMaterial(viewer.getMaterial());
            var data = viewerData.GetValue(viewer) as SkeletonDataAsset;
            if (data == null) return false;
            var skeleton = SpinePlayback.CreateSkeleton(animation.Skeleton, data.GetSkeletonData(false));
            SpinePlayback.Rebind(animation.state, data.GetAnimationStateData());
            RemapViewerLoops(viewer, data.GetSkeletonData(false));
            animation.clearInstructions();
            animation.skeletonDataAsset = data;
            animation.assignReservedSkeleton(skeleton);
            if (context.Body != null)
            {
                string face = AccessTools.Field(typeof(UIPictureBodySpine), "boneface_name").GetValue(context.Body) as string;
                AccessTools.Field(typeof(UIPictureBodySpine), "BoneFace").SetValue(context.Body,
                    face == null ? null : viewer.FindBone(face.Length == 0 ? "face" : face));
            }
            ApplyClipping(viewer);
            animation.Update(0f);
            animation.LateUpdate();
            if (previous != null && previous != data && reserved.GetValue(viewer) is IDictionary cache) cache.Remove(previous);
            return true;
        }

        internal static Texture SelectPortraitPxl(UIPictureBodyData body, Texture original)
        {
            if (!initialized || body is UIPictureBodySpine || original == null
                || !portraitContexts.TryGetValue(body, out var context)) return original;
            context.Image = null;
            if (!Enabled || !pxlSurfaces.TryGetValue(original, out var surface)) return original;
            var identities = new HashSet<string>(surface.Bindings.Select(binding => binding.Address.Identity), StringComparer.Ordinal);
            var selected = selection.Pxl(identities, context.Matches);
            var target = ReferenceEquals(body, pxlPreviewBody) && PreviewTargetEnabled(pxlPreviewTarget)
                && identities.Contains(pxlPreviewTarget.Identity) && context.Matches(pxlPreviewTarget)
                ? pxlPreviewTarget : selected;
            if (target?.PortraitSelection == null) return original;
            if (identities.Any(selection.Invalid)) return original;
            var record = PreparePortraitPxlImage(original, target);
            if (record.Failed || record.Image == null) return original;
            context.Image = record;
            TrackResourceResult(target.Identity, "candidate-applied", null);
            return record.Image;
        }

        private static PortraitPxlImage PreparePortraitPxlImage(Texture original, ReplacementTarget target)
        {
            var record = portraitPxlImages.FirstOrDefault(item => ReferenceEquals(item.Original, original) && item.Target == target);
            if (record == null)
            {
                record = new PortraitPxlImage { Original = original, Target = target };
                portraitPxlImages.Add(record);
                try
                {
                    var bytes = ReplacementPreparation.Texture(target, PatchInfo.ReplaceImagePath,
                        PatchInfo.ReplaceSensitiveImagePath, selection.AllowSensitive, default(System.Threading.CancellationToken));
                    record.Image = new Texture2D(2, 2, TextureFormat.RGBA32, original.mipmapCount > 1, !original.isDataSRGB);
                    if (!record.Image.LoadImage(bytes) || record.Image.width != original.width || record.Image.height != original.height)
                        throw new InvalidDataException("Portrait PXL PNG must match the original page dimensions and layout.");
                    record.Image.filterMode = original.filterMode;
                    record.Image.wrapModeU = original.wrapModeU;
                    record.Image.wrapModeV = original.wrapModeV;
                    record.Image.anisoLevel = original.anisoLevel;
                }
                catch (Exception error)
                {
                    if (record.Image != null) Object.Destroy(record.Image);
                    record.Image = null;
                    record.Failed = true;
                    TrackResourceResult(target.Identity, "candidate-failed", error.Message);
                    BLog.Error("Portrait PXL selection rejected; using the original page.", error);
                }
            }
            return record;
        }

        private static IEnumerable<UIPictureBodyData> LivePortraitBodies()
        {
            portraitBodies.RemoveAll(entry => !(entry.Target is UIPictureBodyData body) || body.PCon?.Gob == null);
            return portraitBodies.Select(entry => entry.Target).OfType<UIPictureBodyData>().ToArray();
        }

        private static void RefreshPortraitPxl()
        {
            var old = portraitPxlImages.ToArray();
            portraitPxlImages.Clear();
            foreach (var body in LivePortraitBodies())
            {
                if (portraitContexts.TryGetValue(body, out var context)) context.Image = null;
                if (!(body is UIPictureBodySpine) && body.Emot?.DefaultTx != null
                    && ReferenceEquals(body.PCon.getBodyData(), body))
                {
                    try { body.PCon.fineCurrentBodyMaterial(); }
                    catch (Exception error) { BLog.Error("Could not refresh portrait PXL material.", error); }
                }
            }
            foreach (var entry in old) if (entry.Image != null) Object.Destroy(entry.Image);
        }

        private static void ReleasePortraitPxl(Texture original)
        {
            foreach (var entry in portraitPxlImages.Where(item => ReferenceEquals(item.Original, original)).ToArray())
            {
                if (entry.Image != null) Object.Destroy(entry.Image);
                portraitPxlImages.Remove(entry);
            }
        }
    }

    [HarmonyPatch]
    internal static class PortraitResourceSelectionPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIPictureBodyData), nameof(UIPictureBodyData.initEmot))]
        private static void Begin(UIPictureBodyData __instance, UIPictureBase.EMSTATE st) =>
            ReplacementRuntime.GuardPxl(() => ReplacementRuntime.BeginPortrait(__instance, st));

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIPictureBodySpine), nameof(UIPictureBodySpine.animRandomize))]
        private static void BeginSpine(UIPictureBodySpine __instance, UIPictureBase.EMSTATE st) =>
            ReplacementRuntime.GuardPxl(() => ReplacementRuntime.BeginPortraitSpine(__instance, st));

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIPictureBodySpine), nameof(UIPictureBodySpine.animRandomize))]
        private static void EndSpine(UIPictureBodySpine __instance) =>
            ReplacementRuntime.GuardPxl(() => ReplacementRuntime.CompletePortraitSpine(__instance));

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIPictureBodyData), "get_texture")]
        private static void Texture(UIPictureBodyData __instance, ref Texture __result)
        {
            try { __result = ReplacementRuntime.SelectPortraitPxl(__instance, __result); }
            catch (Exception error) { BLog.Error("Could not select portrait PXL texture.", error); }
        }
    }
}
