using AICResourceKit.BLogSpace;
using AICResourceKit.Contracts;
using HarmonyLib;
using nel;
using Spine;
using Spine.Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using XX;

namespace AICResourceKit.Patches.ReplaceTexture
{
    internal static partial class ReplacementRuntime
    {
        private sealed class ViewerSpineSource
        {
            internal SpineResourceAddress Address;
            internal SpineAtlasAsset Atlas;
            internal SkeletonDataAsset Data;
        }

        private sealed class ViewerSpineState
        {
            internal ViewerSpineSource Source;
            internal Texture Texture;
            internal Material Material;
            internal bool SharedMaterials;
            internal bool Clipping;
            internal int Attempt = -1;
            internal ViewerSpineBundle Current;
            internal ReplacementWork<PreparedSpine> Pending;
        }

        private static readonly ConditionalWeakTable<SkeletonDataAsset, ViewerSpineSource> viewerSources = new ConditionalWeakTable<SkeletonDataAsset, ViewerSpineSource>();
        private static readonly Dictionary<SpineViewer, ViewerSpineState> ordinaryViewers = new Dictionary<SpineViewer, ViewerSpineState>();
        private static readonly List<ViewerSpineBundle> retiredViewerBundles = new List<ViewerSpineBundle>();
        private static readonly FieldInfo viewerMaterial = AccessTools.Field(typeof(SpineViewer), "Mtr");
        private static readonly FieldInfo viewerRenderer = AccessTools.Field(typeof(SpineViewer), "Mrd");
        private static readonly FieldInfo viewerLoops = AccessTools.Field(typeof(SpineViewer), "Oanm2loop");

        internal static void GuardSpineViewer(Action action)
        {
            if (!initialized) return;
            try { action(); }
            catch (Exception ex) { BLog.Error("Ordinary SpineViewer replacement failed.", ex); }
        }

        internal static SpineResourceAddress ViewerSpineAddress(string atlasKey, string jsonKey, MTISpine mti) =>
            SpineResourceAddress.Create(mti == null ? "resources" : "mti", mti == null ? null : MtiResourceAddress.ContainerKey(mti.resources_path),
                mti == null ? "SpineAnim/" + atlasKey + ".atlas" : mti.atlas_key,
                mti == null ? "SpineAnim/" + jsonKey : jsonKey);

        internal static void RememberSpineAssets(SpineResourceAddress address, SpineAtlasAsset atlas, SkeletonDataAsset data)
        {
            if (address == null || atlas == null || data == null) return;
            RememberAtlasSource(atlas, address.Loader, address.AssetKey, address.AtlasKey);
            viewerSources.Remove(data);
            viewerSources.Add(data, new ViewerSpineSource { Address = address, Atlas = atlas, Data = data });
        }

        internal static void ObserveSpineViewer(SpineViewer viewer, SpineAtlasAsset atlas, SkeletonDataAsset data,
            Texture texture = null, Material material = null)
        {
            if (viewer is SpineViewerNel || data == null || !viewerSources.TryGetValue(data, out var source)) return;
            var animation = animator.GetValue(viewer) as SkeletonAnimation;
            if (animation == null) return;
            if (!ordinaryViewers.TryGetValue(viewer, out var state))
            {
                state = new ViewerSpineState { SharedMaterials = animation.update_sharedmaterials_array,
                    Clipping = clipping != null && (bool)clipping.GetValue(animation) };
                ordinaryViewers.Add(viewer, state);
            }
            bool sourceChanged = state.Source != null && state.Source.Address.Identity != source.Address.Identity;
            if (sourceChanged)
            {
                state.Pending?.Dispose(); state.Pending = null;
                if (state.Current != null) retiredViewerBundles.Add(state.Current);
                state.Current = null;
                state.Attempt = -1;
            }
            if (state.Source?.Data != source.Data || (texture != null && texture != state.Texture)
                || (material != null && material != state.Material))
            { state.Pending?.Dispose(); state.Pending = null; state.Attempt = -1; }
            state.Source = source;
            if (texture != null) state.Texture = texture;
            else if (state.Texture == null) state.Texture = viewer.prepareTexture();
            state.Material = material ?? viewerMaterial.GetValue(viewer) as Material;
            DescribeSpineViewer(state, "entry-hit", "viewer-source-bound");
            UpdateSpineViewer(viewer, state, true, !sourceChanged);
            BindSpineViewer(viewer, state, false);
        }

        internal static bool PrepareOrdinaryMaterial(SpineViewer viewer, Material material, out Material result)
        {
            result = null;
            if (!ordinaryViewers.TryGetValue(viewer, out var state) || state.Current == null) return false;
            if (material != null) state.Material = material;
            viewerMaterial.SetValue(viewer, state.Material);
            BindSpineViewer(viewer, state, false);
            result = state.Material;
            return true;
        }

        internal static bool OrdinaryAssets(SpineViewer viewer, out SpineAtlasAsset atlas, out SkeletonDataAsset data)
        {
            atlas = null; data = null;
            if (!ordinaryViewers.TryGetValue(viewer, out var state) || state.Current == null) return false;
            atlas = state.Current.Atlas; data = state.Current.Data;
            return true;
        }

        internal static bool OrdinaryTexture(SpineViewer viewer, out Texture result)
        {
            result = null;
            if (!ordinaryViewers.TryGetValue(viewer, out var state) || state.Current == null) return false;
            result = state.Current.Materials[0].mainTexture;
            return true;
        }

        internal static bool FineOrdinaryMaterial(SpineViewer viewer)
        {
            if (!ordinaryViewers.TryGetValue(viewer, out var state) || state.Current == null) return false;
            state.Current.SyncMaterials(state.Material);
            return true;
        }

        internal static void CopyOrdinaryViewer(SpineViewer viewer, SpineViewer source)
        {
            if (viewer is SpineViewerNel || !ordinaryViewers.TryGetValue(source, out var state)) return;
            ObserveSpineViewer(viewer, state.Source.Atlas, state.Source.Data, state.Texture);
        }

        internal static void ReleaseOrdinaryViewer(SpineViewer viewer)
        {
            if (!ordinaryViewers.TryGetValue(viewer, out var state)) return;
            state.Pending?.Dispose(); state.Pending = null;
            RestoreSpineViewer(viewer, state);
            ordinaryViewers.Remove(viewer);
            DescribeSpineViewer(state, "released", "viewer-released");
        }

        private static void PumpOrdinaryViewers()
        {
            foreach (var pair in ordinaryViewers.ToArray())
            {
                if (!(animator.GetValue(pair.Key) is SkeletonAnimation animation) || animation == null)
                {
                    ReleaseOrdinaryViewer(pair.Key);
                    continue;
                }
                UpdateSpineViewer(pair.Key, pair.Value, false);
                pair.Value.Current?.SyncMaterials(pair.Value.Material);
            }
            for (int i = retiredViewerBundles.Count - 1; i >= 0; i--)
            {
                var bundle = retiredViewerBundles[i];
                if (ordinaryViewers.Keys.Any(viewer => animator.GetValue(viewer) is SkeletonAnimation animation
                    && animation != null && animation.skeletonDataAsset == bundle.Data)) continue;
                bundle.Dispose(); retiredViewerBundles.RemoveAt(i);
            }
        }

        private static void InvalidateOrdinaryViewers()
        {
            foreach (var state in ordinaryViewers.Values)
            { state.Pending?.Dispose(); state.Pending = null; state.Attempt = -1; }
        }

        private static void UpdateSpineViewer(SpineViewer viewer, ViewerSpineState state, bool firstAccess, bool preservePlayback = true)
        {
            if (state.Attempt == revision && state.Pending == null) return;
            string identity = state.Source.Address.Identity;
            var layers = ActiveLayers(identity);
            ViewerSpineBundle candidate = null;
            try
            {
                if (layers.Count == 0)
                {
                    RestoreSpineViewer(viewer, state); state.Attempt = revision;
                    return;
                }
                if (selection.Invalid(identity)) throw new InvalidOperationException("Invalid Spine target; see catalog errors.");
                if (state.Source.Data == null || state.Source.Atlas == null)
                    throw new InvalidOperationException("Original Spine assets have been released.");
                if (state.Current != null && !CanRetain(state.Current.Sources, identity)) RestoreSpineViewer(viewer, state);
                if (!firstAccess && (scan != null || (state.Pending != null && !state.Pending.IsCompleted))) return;
                PreparedSpine prepared;
                string originalJson = state.Source.Data.skeletonJSON.text, originalAtlas = state.Source.Atlas.atlasFile.text;
                float scale = state.Source.Data.scale;
                string root = PatchInfo.ReplaceImagePath, sensitive = PatchInfo.ReplaceSensitiveImagePath;
                bool allow = selection.AllowSensitive;
                if (firstAccess)
                {
                    state.Pending?.Dispose(); state.Pending = null;
                    prepared = ReplacementPreparation.Spine(originalJson, originalAtlas, scale, layers, root, sensitive, allow, default(System.Threading.CancellationToken), true);
                }
                else
                {
                    if (state.Pending == null)
                    {
                        state.Pending = new ReplacementWork<PreparedSpine>(token => ReplacementPreparation.Spine(
                            originalJson, originalAtlas, scale, layers, root, sensitive, allow, token, true));
                        DescribeSpineViewer(state, "candidate-pending", "preparing");
                    }
                    if (!state.Pending.IsCompleted || !ClaimUpload()) return;
                    state.Pending.TryTake(out prepared, out var error); state.Pending = null;
                    if (error != null) throw error;
                }
                foreach (var layer in layers) ValidateCurrent(layer);
                candidate = ViewerSpineBundle.Build(prepared, layers, state.Source.Atlas, state.Source.Data, state.Texture, state.Material);
                var old = state.Current;
                state.Current = candidate;
                // 游戏切换 JSON 后会自行选择新动画；只有同一来源的热刷新延续旧轨道。
                try { BindSpineViewer(viewer, state, preservePlayback); }
                catch { state.Current = old; throw; }
                candidate = null;
                if (old != null) retiredViewerBundles.Add(old);
                state.Attempt = revision;
                DescribeSpineViewer(state, "candidate-applied", "viewer-pages-bound");
            }
            catch (Exception ex)
            {
                candidate?.Dispose();
                state.Pending?.Dispose(); state.Pending = null;
                state.Attempt = revision;
                if (state.Current != null && !CanRetain(state.Current.Sources, identity)) RestoreSpineViewer(viewer, state);
                DescribeSpineViewer(state, "candidate-failed", ex.GetType().Name, ex.Message);
                BLog.Error("Spine viewer replacement rejected: " + state.Source.Address.JsonKey, ex);
            }
        }

        private static void BindSpineViewer(SpineViewer viewer, ViewerSpineState state, bool refresh)
        {
            var bundle = state.Current;
            if (bundle == null) return;
            bundle.SyncMaterials(state.Material);
            AssignSpineViewer(viewer, state, bundle.Atlas, bundle.Data, bundle.Materials[0].mainTexture, refresh, false);
        }

        private static void RestoreSpineViewer(SpineViewer viewer, ViewerSpineState state)
        {
            state.Pending?.Dispose(); state.Pending = null;
            var old = state.Current;
            if (old == null) return;
            state.Current = null;
            AssignSpineViewer(viewer, state, state.Source.Atlas, state.Source.Data, state.Texture, true, true);
            if (reserved.GetValue(viewer) is IDictionary cache) cache.Remove(old.Data);
            retiredViewerBundles.Add(old);
            DescribeSpineViewer(state, "restored", "original-viewer-assets-restored");
        }

        private static void AssignSpineViewer(SpineViewer viewer, ViewerSpineState state,
            SpineAtlasAsset atlas, SkeletonDataAsset data, Texture texture, bool refresh, bool restoring)
        {
            var animation = animator.GetValue(viewer) as SkeletonAnimation;
            if (animation == null || atlas == null || data == null) return;
            var previousData = animation.skeletonDataAsset;
            // 先验证并重绑状态，再公布资源；轨道对象及其事件监听器保持原引用。
            if (refresh && previousData != null && previousData != data && animation.state != null)
                SpinePlayback.Rebind(animation.state, data.GetAnimationStateData(), restoring);
            viewerAtlas.SetValue(viewer, atlas); viewerData.SetValue(viewer, data);
            viewerContainer.SetValue(viewer, atlas.GetAtlas(false)); viewerTexture.SetValue(viewer, texture);
            animation.update_sharedmaterials_array = restoring ? state.SharedMaterials : true;
            if (clipping != null) clipping.SetValue(animation, state.Clipping || (!restoring && state.Current.Composition.HasClipping));
            if (refresh && previousData != null && previousData != data && animation.state != null)
            {
                var skeleton = SpinePlayback.CreateSkeleton(animation.Skeleton, data.GetSkeletonData(false));
                RemapViewerLoops(viewer, data.GetSkeletonData(false));
                animation.clearInstructions();
                animation.skeletonDataAsset = data;
                animation.assignReservedSkeleton(skeleton);
                animation.Update(0f);
                animation.LateUpdate();
                if (reserved.GetValue(viewer) is IDictionary cache) cache.Remove(previousData);
            }
            if (restoring && viewerRenderer.GetValue(viewer) is MeshRenderer renderer)
                // 原 atlas 可以被多个查看器共享；首材质可能已被另一个查看器写入。
                renderer.sharedMaterials = atlas.materials.Length == 1 ? new[] { state.Material } : atlas.materials;
        }

        private static void RemapViewerLoops(SpineViewer viewer, SkeletonData data)
        {
            if (!(viewerLoops.GetValue(viewer) is IDictionary loops)) return;
            var values = new List<DictionaryEntry>();
            foreach (DictionaryEntry value in loops) values.Add(value);
            loops.Clear();
            foreach (var value in values)
                if (value.Key is Spine.Animation previous && data.FindAnimation(previous.Name) is Spine.Animation current)
                    loops[current] = value.Value;
        }

        private static void DescribeSpineViewer(ViewerSpineState state, string stage, string outcome, string reason = null)
        {
            TrackResourceResult(state.Source.Address.Identity, stage, reason);
            if (!ReplacementDiagnosticRuntime.Enabled) return;
            ReplacementDiagnosticRuntime.Record(ReplacementDiagnosticTarget.SpineAssets(state.Source.Address), stage,
                "SpineViewer / ReplacementRuntime", outcome, reason, new Dictionary<string, object>
                {
                    ["address"] = state.Source.Address.Describe(),
                    ["pageNames"] = (state.Current?.Atlas.GetAtlas(false) ?? PortraitCatalog.ReadAtlas(state.Source.Atlas.atlasFile.text))
                        .Pages.Select(page => page.name).ToArray(),
                    ["packageIds"] = state.Current?.Sources.Select(source => source.Id).ToArray()
                });
        }

        private static void DiscoverOrdinaryViewers()
        {
            foreach (var state in ordinaryViewers.Values) DescribeSpineViewer(state, "discovered", "registered-spine-viewer");
        }
    }
}
