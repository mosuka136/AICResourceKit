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
using System.Threading;
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
                // 渲染在本次绘制稍后发生，临时恢复的原版不会显示；当前组合暂存供收尾时复用。
                if (resource.Current != null) ParkSpine(texture, resource, key);
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
            if (SettlePortraitSpine(viewer, context)) return;
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

        // 在原版选轨后的同一次绘制中决定组合：复用当前或缓存组合，未命中时在主线程同步准备。
        // 返回 false 时交给后台流程（扫描、配置合并、预览或共享查看器等情况）。
        private static bool SettlePortraitSpine(SpineViewerNel viewer, PortraitContext context)
        {
            if (!spineAvailable) return false;
            var texture = viewer.getSvTexture();
            if (texture == null) return false;
            string key = viewer.replace_json_key ?? texture.MtiText.default_json_key;
            string identity = SpineIdentity(texture.key, key);
            if (!spineStates.TryGetValue(texture, out var state))
            {
                if (!HasLayers(identity)) return true;
                spineStates.Add(texture, state = new SpineState());
            }
            var material = viewer.getMaterial();
            var viewerAnimator = animator.GetValue(viewer);
            if (state.Preview != null || HasInvalidLayer(identity) || material == null
                || LiveViewers().Any(other => other != viewer && other.enabled && other.getSvTexture() == texture
                    && !ReferenceEquals(animator.GetValue(other), viewerAnimator))) return false;
            var layers = ActiveLayers(identity).Where(layer => layer.PortraitSelection == null || context.Matches(layer)).ToList();
            bool conditional = layers.Any(layer => layer.PortraitSelection != null);
            if (state.Current != null) return state.JsonKey == key && state.SelectedLayers.SequenceEqual(layers);
            if (layers.Count == 0)
            {
                KeepOriginalPortrait(state, key, layers);
                return true;
            }
            var bundle = state.Variants.Take(layers);
            if (bundle != null && !CanRetain(bundle.Sources, identity))
            {
                retired.Add(bundle);
                bundle = null;
            }
            if (bundle == null)
            {
                if (state.Variants.Failed(layers))
                {
                    KeepOriginalPortrait(state, key, layers);
                    return true;
                }
                if (scan != null || selectionDelay.Waiting || texture.MtiImage0.Image == null) return false;
                try
                {
                    PreparedSpine prepared;
                    Exception failure = null;
                    SkeletonDataAsset original;
                    // 已完成的同组合后台结果直接使用；否则在本次绘制内同步准备，避免先显示原版。
                    if (state.Pending != null && state.PendingKey == key && state.SelectedLayers.SequenceEqual(layers)
                        && state.Pending.IsCompleted)
                    {
                        original = state.PendingOriginal;
                        state.Pending.TryTake(out prepared, out failure);
                        CancelSpinePreparation(state);
                    }
                    else
                    {
                        CancelSpinePreparation(state);
                        prepared = SpinePreparation(texture, key, layers, out original)(CancellationToken.None);
                    }
                    if (failure != null) throw failure;
                    if (original == null) throw new InvalidOperationException("Original Spine data was released while preparing a replacement.");
                    bundle = BuildSpineBundle(texture, layers, material, prepared, original);
                }
                catch (Exception error)
                {
                    CancelSpinePreparation(state);
                    BLog.Error("Spine replacement rejected for " + texture.key + "/" + key + "; using the original portrait.", error);
                    ReplacementDiagnosticRuntime.Spine(texture.key, key, "candidate-failed", error.GetType().Name, error.Message);
                    state.Variants.Fail(layers);
                    KeepOriginalPortrait(state, key, layers);
                    return true;
                }
            }
            bool validated = true;
            try { ValidatePortraitData(bundle.Composition.PreparedData, context.Animations, context.Skins); }
            catch (Exception error)
            {
                validated = false;
                if (conditional)
                {
                    BLog.Error("Selected portrait candidate cannot play this state; using the original portrait.", error);
                    ReplacementDiagnosticRuntime.Spine(texture.key, key, "candidate-failed", error.GetType().Name, error.Message);
                    ParkVariant(state, bundle);
                    KeepOriginalPortrait(state, key, layers);
                    return true;
                }
            }
            CancelSpinePreparation(state);
            state.SelectedLayers = layers;
            state.Conditional = conditional;
            InstallSpine(texture, state, key, bundle);
            if (validated)
            {
                context.PreservePlayback = true;
                if (RebindSelectedPortrait(viewer, context)) return true;
            }
            // 兼容映射后的无条件组合沿用原有的重放方式。
            context.PreservePlayback = false;
            ReplayViewerAnimation(viewer);
            return true;
        }

        private static void KeepOriginalPortrait(SpineState state, string key, List<ReplacementTarget> layers)
        {
            CancelSpinePreparation(state);
            state.SelectedLayers = layers;
            state.Conditional = layers.Any(layer => layer.PortraitSelection != null);
            state.JsonKey = key;
            state.Attempt = revision;
        }

        private static bool ReplaySelectedPortrait(SpineViewerNel viewer)
        {
            if (!portraitSpineContexts.TryGetValue(viewer, out var context) || context.Collecting || !context.PreservePlayback) return false;
            return RebindSelectedPortrait(viewer, context);
        }

        // 沿用 AnimationState 与轨道，把查看器重绑到当前显示的组合。
        private static bool RebindSelectedPortrait(SpineViewerNel viewer, PortraitContext context)
        {
            if (!(animator.GetValue(viewer) is SkeletonAnimation animation) || animation.state == null) return false;
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
