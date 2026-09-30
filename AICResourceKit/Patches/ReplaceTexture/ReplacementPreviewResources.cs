using AICResourceKit.BLogSpace;
using HarmonyLib;
using nel;
using Spine.Unity;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using XX;

namespace AICResourceKit.Patches.ReplaceTexture
{
    internal static partial class ReplacementRuntime
    {
        private sealed class PreviewBuild
        {
            internal ReplacementTarget Target;
            internal BetobetoManager.SvTexture Texture;
            internal int Revision;
            internal List<ReplacementTarget> Layers;
            internal SkeletonDataAsset Original;
            internal ReplacementWork<PreparedSpine> Work;
            internal SpineBundle Candidate;
            internal UIPictureBodyData Body;
            internal ReplacementPreviewPose Pose;
            internal PortraitPxlImage PxlImage;
            internal bool Activating, Returning, Prepared, Applied;
            internal Exception Error;
        }

        private const string PreviewLoadKey = "_AICResourceKit_Preview";
        private static readonly HashSet<BetobetoManager.SvTexture> previewHeld = new HashSet<BetobetoManager.SvTexture>();
        private static PreviewBuild previewBuild;
        private static UIPictureBodyData pxlPreviewBody;
        private static ReplacementTarget pxlPreviewTarget;

        internal static ReplacementPreviewPose DescribePreview(UIPictureBodyData body, PortraitSelection requested)
        {
            if (body == null) return null;
            var result = new ReplacementPreviewPose { Selection = requested };
            if (body is UIPictureBodySpine spine)
            {
                var viewer = spine.getViewer();
                var texture = viewer?.getSvTexture();
                if (texture == null) return null;
                result.SpineKey = texture.key;
                result.JsonKey = viewer.replace_json_key ?? texture.MtiText.default_json_key;
                var names = AccessTools.Field(typeof(UIPictureBodySpine), "Abase_anim").GetValue(spine) as string[] ?? new string[0];
                string current = result.Animation = viewer.getBaseAnimName();
                result.Animations = names.OrderByDescending(name => name == current).ToArray();
            }
            else if (body.Emot?.DefaultTx != null && pxlSurfaces.TryGetValue(body.Emot.DefaultTx, out var surface))
                result.PxlIdentities.UnionWith(surface.Bindings.Select(binding => binding.Address.Identity));
            return result;
        }

        internal static void CancelPendingResourcePreview() => CancelPreviewBuild();

        // 这里仅创建独立资源，不安装到 SvTexture，也不切换当前立绘或改动其材质。
        internal static PortraitPreviewReadiness PrepareResourcePreview(UIPictureBodyData body,
            ReplacementTarget target, Material material, ReplacementPreviewPose pose = null)
        {
            if (!PreviewTargetEnabled(target)) return PortraitPreviewReadiness.Failed;
            if (body == null) return PortraitPreviewReadiness.Failed;
            pose = pose ?? DescribePreview(body, new PortraitSelection(body.Emot.emot_id, body.PCon.getCurrentState()));
            if (pose == null || ReplacementPreview.Choose(new[] { target }, new[] { pose }, out _) == null)
                return PortraitPreviewReadiness.Failed;
            if (target.Loader == "pxl")
            {
                if (pose.PxlIdentities.Any(selection.Invalid)) return PortraitPreviewReadiness.Failed;
                selection.Pxl(pose.PxlIdentities, pose.Matches); // 同一贴图不能由一个包的多个地址重复命中。
                if (ReferenceEquals(pxlPreviewBody, body) && ReferenceEquals(pxlPreviewTarget, target))
                    return PreviewReadiness(body, target);
                var image = PreparePortraitPxlImage(body.Emot.DefaultTx, target);
                if (image.Failed || image.Image == null) return PortraitPreviewReadiness.Failed;
                CancelPreviewBuild();
                previewBuild = new PreviewBuild { Target = target, Body = body, Pose = pose, Revision = revision, PxlImage = image };
                return PortraitPreviewReadiness.Ready;
            }
            var viewer = (body as UIPictureBodySpine)?.getViewer();
            var texture = viewer?.getSvTexture();
            if (texture == null || material == null || texture.key != target.SpineKey
                || (viewer.replace_json_key ?? texture.MtiText.default_json_key) != target.JsonKey)
                return PortraitPreviewReadiness.Failed;
            var layers = ReplacementPreview.Layers(selection, target, pose);
            if (layers.Count == 0) return PortraitPreviewReadiness.Failed;
            var ready = PrepareSpinePreview(body, material, pose, layers, target);
            return ready == PortraitPreviewReadiness.Ready && previewBuild.Applied ? PreviewReadiness(body, target) : ready;
        }

        // 预览与恢复复用同一准备流程；准备期间不改变正在显示的资源。
        private static PortraitPreviewReadiness PrepareSpinePreview(UIPictureBodyData body, Material material,
            ReplacementPreviewPose pose, List<ReplacementTarget> layers, ReplacementTarget target = null)
        {
            var texture = ((UIPictureBodySpine)body).getViewer().getSvTexture();
            if (previewBuild == null || !ReferenceEquals(previewBuild.Target, target)
                || !ReferenceEquals(previewBuild.Body, body) || previewBuild.Texture != texture || previewBuild.Revision != revision
                || !previewBuild.Pose.Selection.Equals(pose.Selection) || previewBuild.Pose.Animation != pose.Animation)
            {
                CancelPreviewBuild();
                if (previewHeld.Add(texture))
                {
                    texture.MtiImage0.addLoadKey(PreviewLoadKey, false);
                    texture.MtiText.addLoadKey(PreviewLoadKey);
                }
                previewBuild = new PreviewBuild
                {
                    Target = target, Texture = texture, Revision = revision, Layers = layers, Body = body, Pose = pose,
                    Returning = target == null, Prepared = layers.Count == 0
                };
                if (layers.Count > 0)
                {
                    SpineViewer.prepareAtlasAssetsS(texture.MtiText, out var atlas, out var original, pose.JsonKey);
                    string json = original.skeletonJSON.text, atlasText = atlas.atlasFile.text;
                    float scale = original.scale;
                    string root = PatchInfo.ReplaceImagePath, sensitive = PatchInfo.ReplaceSensitiveImagePath;
                    bool allowSensitive = selection.AllowSensitive;
                    previewBuild.Original = original;
                    previewBuild.Work = new ReplacementWork<PreparedSpine>(token => ReplacementPreparation.Spine(
                        json, atlasText, scale, layers, root, sensitive, allowSensitive, token));
                }
            }
            if (previewBuild.Prepared) return PortraitPreviewReadiness.Ready;
            if (previewBuild.Work == null) return PortraitPreviewReadiness.Failed;
            if (!previewBuild.Work.IsCompleted || !ClaimUpload()) return PortraitPreviewReadiness.Loading;
            previewBuild.Work.TryTake(out var prepared, out var error);
            previewBuild.Work = null;
            if (error != null) throw error;
            if (previewBuild.Original == null) throw new InvalidOperationException("Original preview data was released.");
            previewBuild.Candidate = BuildSpineBundle(texture, previewBuild.Layers, material, prepared, previewBuild.Original);
            previewBuild.Prepared = true;
            return PortraitPreviewReadiness.Ready;
        }

        internal static void ActivateResourcePreview(UIPictureBodyData body, ReplacementTarget target)
        {
            if (!PreviewTargetEnabled(target) || previewBuild == null || !ReferenceEquals(previewBuild.Body, body)
                || !ReferenceEquals(previewBuild.Target, target) || previewBuild.Revision != revision)
                throw new InvalidOperationException("Preview resources are not ready for this selection.");
            RemoveResourcePreview();
            if (target.Loader == "pxl")
            {
                if (previewBuild.PxlImage?.Image == null) throw new InvalidOperationException("Preview PXL image was released.");
                pxlPreviewBody = body;
                pxlPreviewTarget = target;
                return;
            }
            if (previewBuild.Candidate == null) throw new InvalidOperationException("Preview Spine candidate is missing.");
            // 原版先选出当前状态的完整轨道；在同一次绘制内校验并切到已准备好的候选。
            previewBuild.Activating = true;
        }

        private static bool IsActivatingResourcePreview(UIPictureBodySpine body) =>
            previewBuild?.Activating == true && ReferenceEquals(previewBuild.Body, body);

        internal static string PreviewAnimation(SpineViewerNel viewer, string original) =>
            previewBuild?.Activating == true && (previewBuild.Body as UIPictureBodySpine)?.getViewer() == viewer
                ? previewBuild.Pose.Animation ?? original : original;

        private static bool CompleteResourcePreview(UIPictureBodySpine body, PortraitContext context)
        {
            if (!IsActivatingResourcePreview(body)) return false;
            var pending = previewBuild;
            try
            {
                if (pending.Layers.Any(layer => !context.Matches(layer)))
                    throw new InvalidOperationException("The actual portrait does not match the preview conditions.");
                if (pending.Candidate != null)
                    ValidatePortraitData(pending.Candidate.Composition.PreparedData, context.Animations, context.Skins);
                var texture = body.getViewer().getSvTexture();
                if (!spineStates.TryGetValue(texture, out var state)) spineStates.Add(texture, state = new SpineState());
                CancelSpinePreparation(state);
                if (pending.Returning)
                {
                    state.SelectedLayers = pending.Layers;
                    state.Conditional = pending.Layers.Any(layer => layer.PortraitSelection != null);
                    InstallSpine(texture, state, pending.Pose.JsonKey, pending.Candidate);
                }
                else
                {
                    var previous = state.Shown;
                    state.Preview = pending.Candidate;
                    state.PreviewTarget = pending.Target;
                    RebindShown(texture, state, previous);
                }
                pending.Candidate = null;
                context.PreservePlayback = true;
                ReplaySelectedPortrait(body.getViewer());
                pending.Applied = true;
            }
            catch (Exception error) { pending.Error = error; }
            finally { pending.Activating = false; }
            return true;
        }

        internal static void VerifyResourcePreview(UIPictureBodyData body, ReplacementTarget target)
        {
            if (previewBuild?.Error != null) throw new InvalidOperationException("Preview state cannot use the candidate.", previewBuild.Error);
            if (PreviewReadiness(body, target) != PortraitPreviewReadiness.Ready)
                throw new InvalidOperationException("Preview resources were not bound to the portrait.");
        }

        // 恢复也先按原姿态和实际动画准备正常排序的组合，再一次切换完整画面。
        internal static bool PreparePreviewReturn(UIPictureBodyData body, Material material, ReplacementPreviewPose pose)
        {
            if (!(body is UIPictureBodySpine)) return true;
            string identity = SpineIdentity(pose.SpineKey, pose.JsonKey);
            var layers = HasInvalidLayer(identity) ? new List<ReplacementTarget>()
                : ActiveLayers(identity).Where(pose.Matches).ToList();
            return PrepareSpinePreview(body, material, pose, layers) == PortraitPreviewReadiness.Ready;
        }

        internal static void ActivatePreviewReturn(UIPictureBodyData body)
        {
            RemoveResourcePreview();
            if (!(body is UIPictureBodySpine)) return;
            if (previewBuild?.Returning != true || !previewBuild.Prepared || previewBuild.Revision != revision
                || !ReferenceEquals(previewBuild.Body, body))
                throw new InvalidOperationException("Original portrait resources are not ready.");
            previewBuild.Activating = true;
        }

        internal static void VerifyPreviewReturn(UIPictureBodyData body)
        {
            if (!(body is UIPictureBodySpine)) return;
            if (previewBuild?.Error != null || previewBuild?.Applied != true)
                throw new InvalidOperationException("Original portrait could not be restored.", previewBuild?.Error);
        }

        internal static void RemoveResourcePreview()
        {
            if (previewBuild != null) previewBuild.Activating = false;
            var previousPxl = pxlPreviewBody;
            pxlPreviewBody = null;
            pxlPreviewTarget = null;
            if (previousPxl?.PCon?.Gob != null && previousPxl.Emot?.DefaultTx != null
                && ReferenceEquals(previousPxl.PCon.getBodyData(), previousPxl)) previousPxl.PCon.fineCurrentBodyMaterial();
            foreach (var pair in spineStates.Where(pair => pair.Value.Preview != null).ToArray())
                RemoveResourcePreview(pair.Key, pair.Value);
        }

        private static void RemoveResourcePreview(BetobetoManager.SvTexture texture, SpineState state)
        {
            var old = state.Preview;
            state.Preview = null;
            state.PreviewTarget = null;
            try { RebindShown(texture, state, old); }
            finally { retired.Add(old); }
        }

        private static void RevokeUnauthorizedPreviews()
        {
            if (pxlPreviewTarget != null && !PreviewTargetEnabled(pxlPreviewTarget)) RemoveResourcePreview();
            foreach (var pair in spineStates.Where(pair => pair.Value.Preview != null).ToArray())
                if (!PreviewTargetEnabled(pair.Value.PreviewTarget)
                    || !CanRetain(pair.Value.Preview.Sources, pair.Value.PreviewTarget.Identity))
                {
                    RemoveResourcePreview(pair.Key, pair.Value);
                    // 恢复原姿态可能还需加载，先撤销正在显示的临时资源。
                    foreach (var viewer in LiveViewers().Where(viewer => viewer.enabled && viewer.getSvTexture() == pair.Key))
                    {
                        try { ReplayViewerAnimation(viewer); }
                        catch (Exception ex) { BLog.Error("Could not redraw a revoked resource preview.", ex); }
                    }
                }
        }

        private static void CancelPreviewBuild()
        {
            var pending = previewBuild;
            previewBuild = null;
            pending?.Work?.Dispose();
            pending?.Candidate?.Dispose();
        }

        internal static void FinishResourcePreview()
        {
            try { RemoveResourcePreview(); }
            finally
            {
                CancelPreviewBuild();
                foreach (var texture in previewHeld.ToArray())
                {
                    previewHeld.Remove(texture);
                    try { texture.MtiImage0.remLoadKey(PreviewLoadKey); }
                    finally { texture.MtiText.remLoadKey(PreviewLoadKey); }
                }
            }
        }
    }
}
