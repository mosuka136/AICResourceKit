using AICResourceKit.BLogSpace;
using HarmonyLib;
using nel;
using Spine.Unity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using XX;
using Object = UnityEngine.Object;

namespace AICResourceKit.Patches.ReplaceTexture
{
    // 主立绘替换的准备、安装与释放；显示和资源构建分别由 Viewers、SpineAssets 负责。
    internal static partial class ReplacementRuntime
    {
        private sealed class SpineState
        {
            internal SpineBundle Current;
            internal SpineBundle Preview;
            internal ReplacementTarget PreviewTarget;
            internal SpineBundle Shown => Preview ?? Current;
            internal string ShownKey => Preview != null ? PreviewTarget.JsonKey : JsonKey;
            internal int Attempt = -1;
            internal string JsonKey;
            internal bool Conditional;
            internal List<ReplacementTarget> SelectedLayers = new List<ReplacementTarget>();
            internal string PendingKey;
            internal ReplacementWork<PreparedSpine> Pending;
            internal SkeletonDataAsset PendingOriginal;
            internal float PendingSince;
        }

        private static readonly Dictionary<BetobetoManager.SvTexture, SpineState> spineStates =
            new Dictionary<BetobetoManager.SvTexture, SpineState>();
        private static readonly List<SpineBundle> retired = new List<SpineBundle>();
        private static bool spineAvailable;
        private static readonly FieldInfo svAtlas = AccessTools.Field(typeof(BetobetoManager.SvTexture), "SpAtlasAsset");
        private static readonly FieldInfo svData = AccessTools.Field(typeof(BetobetoManager.SvTexture), "SpDataAsset");
        private static readonly FieldInfo depth = AccessTools.Field(typeof(BetobetoManager.SvTexture), "AZBufRect");
        private static readonly MethodInfo allocate = AccessTools.Method(typeof(BetobetoManager.SvTexture), "allocTexture");

        internal static bool HasActive(BetobetoManager.SvTexture texture)
        {
            return texture != null && spineStates.TryGetValue(texture, out var state) && state.Shown != null;
        }

        internal static bool Prepare(BetobetoManager.SvTexture texture, string jsonKey, Material[] materials,
            out SpineAtlasAsset atlas, out SkeletonDataAsset data)
        {
            atlas = null;
            data = null;
            if (!spineAvailable || texture == null || materials == null || materials.Length == 0) return false;
            string key = jsonKey ?? texture.MtiText.default_json_key;
            if (!spineStates.TryGetValue(texture, out var existing) || (existing.Preview == null && existing.Attempt < 0))
                UpdateSpine(texture, key, materials[0], null);
            if (!spineStates.TryGetValue(texture, out var state) || state.Shown == null) return false;
            if (state.ShownKey != key) throw new InvalidOperationException("Spine JSON variant changed outside a safe switch.");
            state.Shown.Bind(materials);
            svAtlas.SetValue(texture, state.Shown.Atlas);
            svData.SetValue(texture, state.Shown.Data);
            texture.preapreAtlasDepth();
            atlas = state.Shown.Atlas;
            data = state.Shown.Data;
            if (ReplacementDiagnosticRuntime.Enabled)
                ReplacementDiagnosticRuntime.Record(ReplacementDiagnosticTarget.Spine(texture.key, key),
                    "candidate-applied", "ReplacementRuntime.Prepare", "atlas-and-data-bound",
                    details: new Dictionary<string, object>
                    {
                        ["packageIds"] = state.Shown.Sources.Select(source => source.Id).ToArray(),
                        ["temporaryPreview"] = state.Preview != null
                    });
            return true;
        }

        private static bool UpdateSpine(BetobetoManager.SvTexture texture, string key, Material material,
            SpineViewerNel switching)
        {
            if (!spineStates.TryGetValue(texture, out var state)) spineStates.Add(texture, state = new SpineState());
            var context = PortraitContextFor(texture, switching);
            var layers = ActiveLayers(SpineIdentity(texture.key, key))
                .Where(layer => layer.PortraitSelection == null || (context != null && context.Matches(layer))).ToList();
            bool layersChanged = !state.SelectedLayers.SequenceEqual(layers);
            bool restoreConditional = layersChanged && state.Conditional && state.Current != null;
            if (layersChanged)
            {
                CancelSpinePreparation(state);
                state.SelectedLayers = layers;
                state.Conditional = layers.Any(layer => layer.PortraitSelection != null);
                state.Attempt = -1;
            }
            if (state.Pending != null && state.PendingKey != key) { CancelSpinePreparation(state); state.Attempt = -1; }
            if (state.Attempt == revision && state.JsonKey == key && state.Pending == null) return false;
            if (selectionDelay.Waiting && state.JsonKey == key) return false;
            if (material == null) return false;
            var switchingAnimator = switching == null ? null : animator.GetValue(switching);
            if (state.Preview == null && LiveViewers().Any(viewer => viewer != switching && viewer.enabled && viewer.getSvTexture() == texture
                && !ReferenceEquals(animator.GetValue(viewer), switchingAnimator))) return false;
            bool restored = restoreConditional && InstallSpine(texture, state, key, null);
            var old = state.Current;
            string identity = SpineIdentity(texture.key, key);
            SpineBundle candidate = null;
            bool damaged = HasInvalidLayer(identity) || (old != null && HasUnidentifiedErrors(old.Sources, identity));
            if (damaged)
            {
                CancelSpinePreparation(state);
                BLog.Warn("Spine replacement target is damaged: " + texture.key + "/" + key + ".");
                ReplacementDiagnosticRuntime.Spine(texture.key, key, "candidate-failed", "invalid-layer", "Invalid or unidentified manifest target; see catalog errors.");
                if (old != null && state.JsonKey == key && CanRetain(old.Sources, identity))
                {
                    state.Attempt = revision;
                    return restored;
                }
            }
            else if (layers.Count > 0)
            {
                try
                {
                    if (scan != null)
                    {
                        if (old != null && !CanRetain(old.Sources, identity)) return InstallSpine(texture, state, key, null) || restored;
                        return restored;
                    }
                    if (state.Pending == null)
                    {
                        texture.MtiText.addLoadKey("_SV");
                        texture.MtiImage0.addLoadKey("_SV", false);
                        SpineViewer.prepareAtlasAssetsS(texture.MtiText, out var originalAtlas, out var originalData, key);
                        state.PendingOriginal = originalData;
                        string originalJson = originalData.skeletonJSON.text;
                        string originalAtlasText = originalAtlas.atlasFile.text;
                        float originalScale = originalData.scale;
                        string root = PatchInfo.ReplaceImagePath, sensitive = PatchInfo.ReplaceSensitiveImagePath;
                        bool allow = selection.AllowSensitive;
                        state.PendingKey = key;
                        state.PendingSince = Time.unscaledTime;
                        state.Pending = new ReplacementWork<PreparedSpine>(token => ReplacementPreparation.Spine(
                            originalJson, originalAtlasText, originalScale, layers, root, sensitive, allow, token));
                        ReplacementDiagnosticRuntime.Spine(texture.key, key, "candidate-pending", "preparing");
                        state.Attempt = revision;
                    }
                    if (!state.Pending.IsCompleted || !ClaimUpload())
                    {
                        if (old == null) { state.JsonKey = key; return restored; }
                        if (state.JsonKey == key && CanRetain(old.Sources, identity)) return restored;
                        return InstallSpine(texture, state, key, null) || restored;
                    }
                    var original = state.PendingOriginal;
                    state.Pending.TryTake(out var prepared, out var error);
                    CancelSpinePreparation(state);
                    if (error != null) throw error;
                    if (original == null) throw new InvalidOperationException("Original Spine data was released while preparing a replacement.");
                    if (state.Conditional)
                    {
                        if (context != null) context.PreservePlayback = true;
                        if (context == null || context.Collecting) throw new InvalidOperationException("Portrait state changed while preparing.");
                        ValidatePortraitData(prepared.Composition.PreparedData, context.Animations, context.Skins);
                    }
                    candidate = BuildSpineBundle(texture, layers, material, prepared, original);
                }
                catch (Exception ex)
                {
                    CancelSpinePreparation(state);
                    BLog.Error("Spine replacement rejected for " + texture.key + "/" + key
                        + "; retaining the last usable composition when authorized.", ex);
                    ReplacementDiagnosticRuntime.Spine(texture.key, key, "candidate-failed", ex.GetType().Name, ex.Message);
                    if (old != null && state.JsonKey == key
                        && CanRetain(old.Sources, identity))
                    {
                        state.Attempt = revision;
                        return restored;
                    }
                }
            }
            else
            {
                CancelSpinePreparation(state);
                if (old != null && state.JsonKey == key && CanRetain(old.Sources, identity))
                {
                    state.Attempt = revision;
                    return restored;
                }
            }
            return InstallSpine(texture, state, key, candidate) || restored;
        }

        private static bool InstallSpine(BetobetoManager.SvTexture texture, SpineState state, string key, SpineBundle candidate)
        {
            var old = state.Current;
            state.Attempt = revision;
            state.JsonKey = key;
            if (old == null && candidate == null) return false;
            state.Current = candidate;
            // 正常组合继续后台更新，但不能盖住临时预览。
            if (state.Preview != null)
            {
                ForgetReserved(texture, old);
                if (old != null) retired.Add(old);
                return false;
            }
            RebindShown(texture, state, old);
            ReplacementDiagnosticRuntime.Spine(texture.key, key, candidate == null ? "restored" : "candidate-pending",
                candidate == null ? "original-restored" : "composition-installed-awaiting-bind");
            if (old != null) retired.Add(old);
            BLog.Info(candidate == null ? "Spine replacement restored: " + texture.key
                : "Spine replacement activated: " + string.Join(" + ", candidate.Sources.Select(source => source.Id)));
            return true;
        }

        private static void RebindShown(BetobetoManager.SvTexture texture, SpineState state, SpineBundle old)
        {
            Invalidate(texture, old);
            var rendered = texture.getRendered();
            texture.releaseTexture();
            if (rendered != null) Object.Destroy(rendered);
            depth.SetValue(texture, null);
            texture.atlas_depth_written = false;
            svAtlas.SetValue(texture, state.Shown?.Atlas);
            svData.SetValue(texture, state.Shown?.Data);
            displayRevision++;
        }

        private static void CancelSpinePreparation(SpineState state)
        {
            state.Pending?.Dispose();
            state.Pending = null;
            state.PendingOriginal = null;
            state.PendingKey = null;
        }

        internal static bool Clean(BetobetoManager.SvTexture texture)
        {
            if (!spineStates.TryGetValue(texture, out var state) || state.Shown == null) return false;
            var image = state.Shown.Image;
            var previous = RenderTexture.active;
            try
            {
                allocate.Invoke(texture, new object[] { image.width, image.height, texture.key });
                var target = texture.getRendered();
                BLIT.PasteTo(target, image, target.width * 0.5f, target.height * 0.5f, 1f);
                texture.dirt_index = 0;
                return true;
            }
            finally { RenderTexture.active = previous; }
        }

        internal static bool DirtEnabled(BetobetoManager.SvTexture texture)
        {
            return !spineStates.TryGetValue(texture, out var state) || state.Shown == null
                || state.Shown.Composition.DirtEnabled;
        }

        internal static void Released(BetobetoManager.SvTexture texture)
        {
            if (texture != null && ReplacementDiagnosticRuntime.Enabled)
            {
                spineStates.TryGetValue(texture, out var releasedState);
                ReplacementDiagnosticRuntime.Spine(texture.key, releasedState?.ShownKey, "released", "atlas-data-released");
            }
            if (previewBuild?.Texture == texture) CancelPreviewBuild();
            if (!spineStates.TryGetValue(texture, out var state)) return;
            CancelSpinePreparation(state);
            Invalidate(texture, state.Shown);
            ForgetReserved(texture, state.Current);
            if (state.Current != null) retired.Add(state.Current);
            if (state.Preview != null) retired.Add(state.Preview);
            spineStates.Remove(texture);
            depth.SetValue(texture, null);
            Collect();
        }

        internal static void Collect()
        {
            if (retired.Count == 0) return;
            var live = LiveViewers().Select(viewer => animator.GetValue(viewer) as SkeletonAnimation)
                .Where(animation => animation != null).ToArray();
            for (int i = retired.Count - 1; i >= 0; i--)
            {
                if (live.Any(animation => animation.skeletonDataAsset == retired[i].Data)) continue;
                retired[i].Dispose();
                retired.RemoveAt(i);
            }
        }

        private static void InvalidateSpineSelection(ReplacementSelection previous, bool force)
        {
            foreach (var pair in spineStates)
            {
                var state = pair.Value;
                string identity = SpineIdentity(pair.Key.key, state.PendingKey ?? state.JsonKey);
                if (!force && previous.SameSpine(selection, identity)
                    && (state.Current == null || CanRetain(state.Current.Sources, identity)))
                {
                    if (state.Attempt >= 0) state.Attempt = revision;
                }
                else
                {
                    CancelSpinePreparation(state);
                    state.Attempt = -1;
                }
            }
        }
    }
}
