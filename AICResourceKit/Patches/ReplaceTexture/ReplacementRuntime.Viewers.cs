using AICResourceKit.BLogSpace;
using HarmonyLib;
using nel;
using Spine;
using Spine.Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using XX;

namespace AICResourceKit.Patches.ReplaceTexture
{
    // 刷新立绘消费者、保留动画与皮肤状态，并应用显示参数。
    internal static partial class ReplacementRuntime
    {
        private static readonly List<WeakReference> viewers = new List<WeakReference>();
        private static readonly FieldInfo viewerAtlas = AccessTools.Field(typeof(SpineViewer), "SpAtlasAsset");
        private static readonly FieldInfo viewerData = AccessTools.Field(typeof(SpineViewer), "SpDataAsset");
        private static readonly FieldInfo viewerContainer = AccessTools.Field(typeof(SpineViewer), "AtlasContainer");
        private static readonly FieldInfo viewerTexture = AccessTools.Field(typeof(SpineViewer), "Tex");
        private static readonly FieldInfo animator = AccessTools.Field(typeof(SpineViewer), "charaAnim");
        private static readonly FieldInfo reserved = AccessTools.Field(typeof(SpineViewer), "ORsvData");
        private static readonly FieldInfo clipping = AccessTools.Field(typeof(SkeletonRenderer), "useClipping");

        private static void PumpSpines()
        {
            var visible = new HashSet<BetobetoManager.SvTexture>();
            foreach (var viewer in LiveViewers())
            {
                try
                {
                    var texture = viewer.getSvTexture();
                    if (texture == null || !viewer.enabled) continue;
                    visible.Add(texture);
                    if (!spineStates.TryGetValue(texture, out var state)) continue;
                    if (state.Pending == null ? state.Attempt >= 0 : !state.Pending.IsCompleted) continue;
                    string key = viewer.replace_json_key ?? texture.MtiText.default_json_key;
                    if ((state.Pending == null || key == state.PendingKey) && viewer.getBaseAnimName() != null
                        && UpdateSpine(texture, key, viewer.getMaterial(), viewer)) ReplayViewerAnimation(viewer);
                }
                catch (Exception ex) { BLog.Error("Replacement refresh failed for one Spine viewer.", ex); }
            }
            // 已离开画面的请求不长期持有临时明文；再次显示时可重新准备。
            foreach (var pair in spineStates)
                if (!visible.Contains(pair.Key) && pair.Value.Pending != null
                    && Time.unscaledTime - pair.Value.PendingSince > 15f)
                { CancelSpinePreparation(pair.Value); pair.Value.Attempt = -1; }
        }

        private static void ReplayViewerAnimation(SpineViewerNel viewer)
        {
            if (ReplaySelectedPortrait(viewer)) return;
            string animation = viewer.getBaseAnimName();
            if (animation == null) return;
            string[] skins = CaptureSkinNames(viewer.GetSkeleton()?.SkinList);
            var entry = viewer.getTrack(0);
            int loopFrame = entry?.Animation == null ? -1000 : viewer.getAnmLoopFrame(entry.Animation);
            viewer.clearAnim(animation, loopFrame, skins.FirstOrDefault());
            viewer.mergeSkins(skins);
        }

        internal static string[] CaptureSkinNames(IEnumerable<Skin> skins)
        {
            if (skins == null) return new string[0];
            return skins.Where(skin => skin != null && !string.IsNullOrEmpty(skin.Name))
                .Select(skin => skin.Name).Distinct(StringComparer.Ordinal).ToArray();
        }

        internal static void Register(SpineViewerNel viewer)
        {
            if (!initialized || viewer == null) return;
            if (!viewers.Any(weak => ReferenceEquals(weak.Target, viewer))) viewers.Add(new WeakReference(viewer));
        }

        internal static void BeforeSwitch(SpineViewerNel viewer, string jsonKey = null)
        {
            if (!spineAvailable || viewer == null) return;
            Register(viewer);
            var texture = viewer.getSvTexture();
            if (texture == null) return;
            string key = jsonKey ?? viewer.replace_json_key ?? texture.MtiText.default_json_key;
            UpdateSpine(texture, key, viewer.getMaterial(), viewer);
            if (jsonKey != null && spineStates.TryGetValue(texture, out var state)
                && state.Shown != null && state.ShownKey == key)
            {
                viewerAtlas.SetValue(viewer, state.Shown.Atlas);
                viewerData.SetValue(viewer, state.Shown.Data);
                viewerContainer.SetValue(viewer, state.Shown.Atlas.GetAtlas(false));
            }
            ApplyClipping(viewer);
        }

        internal static string MapBone(SpineViewer viewer, string name)
        {
            if (name != null && ordinaryViewers.TryGetValue(viewer, out var ordinary) && ordinary.Current != null
                && ordinary.Current.Composition.BoneMap.TryGetValue(name, out string ordinaryName)) return ordinaryName;
            if (name == null || !(viewer is SpineViewerNel nelViewer)) return name;
            var texture = nelViewer.getSvTexture();
            if (texture != null && spineStates.TryGetValue(texture, out var state) && state.Shown != null
                && state.Shown.Composition.BoneMap.TryGetValue(name, out string mapped)) return mapped;
            return name;
        }

        internal static float ApplyDisplay(UIPictureBodySpine body, string property, float value)
        {
            var texture = body?.getViewer()?.getSvTexture();
            if (texture == null || !spineStates.TryGetValue(texture, out var state) || state.Shown == null) return value;
            var display = state.Shown.Composition.Display;
            switch (property)
            {
                case "scale": return value * (display.ScaleMultiplier ?? 1f);
                case "shift_ux": return value + (display.OffsetX ?? 0f) / 64f;
                case "shift_uy": return value + (display.OffsetY ?? 0f) / 64f;
                case "base_swidth":
                    return display.Width.HasValue ? value * display.Width.Value / GetPrivateFloat(body, "swidth") : value;
                case "base_sheight":
                    return display.Height.HasValue ? value * display.Height.Value / GetPrivateFloat(body, "sheight") : value;
                default: return value;
            }
        }

        private static float GetPrivateFloat(object instance, string name)
        {
            var field = AccessTools.Field(instance.GetType(), name);
            if (field == null) return 1f;
            float value = (float)field.GetValue(instance);
            return value == 0 ? 1f : value;
        }

        internal static void ApplyRightShift(UIPictureBodySpine body, ref float value)
        {
            var texture = body?.getViewer()?.getSvTexture();
            if (texture == null || !spineStates.TryGetValue(texture, out var state) || state.Shown == null
                || !state.Shown.Composition.Display.RightShift.HasValue) return;
            float old = body.rightshift_px;
            float side = GetPositionRight(body);
            value += side * (state.Shown.Composition.Display.RightShift.Value - old) / 64f;
        }

        private static float GetPositionRight(UIPictureBodySpine body)
        {
            var field = AccessTools.Field(typeof(UIPictureBodyData), "PCon");
            object controller = field?.GetValue(body);
            var property = controller == null ? null : AccessTools.Property(controller.GetType(), "is_position_right");
            if (property != null) return Convert.ToSingle(property.GetValue(controller, null));
            var side = controller == null ? null : AccessTools.Field(controller.GetType(), "is_position_right");
            return side == null ? 0f : Convert.ToSingle(side.GetValue(controller));
        }

        private static void ApplyClipping(SpineViewerNel viewer)
        {
            if (clipping == null || viewer == null) return;
            var texture = viewer.getSvTexture();
            bool enabled = texture != null && spineStates.TryGetValue(texture, out var state)
                && state.Shown != null && state.Shown.Composition.HasClipping;
            if (animator.GetValue(viewer) is SkeletonRenderer renderer) clipping.SetValue(renderer, enabled);
        }

        internal static void AfterSwitch(SpineViewerNel viewer)
        {
            ApplyClipping(viewer);
            Collect();
        }

        private static IEnumerable<SpineViewerNel> LiveViewers()
        {
            viewers.RemoveAll(weak => !weak.IsAlive);
            return viewers.Select(weak => weak.Target).OfType<SpineViewerNel>().ToArray();
        }

        private static void Invalidate(BetobetoManager.SvTexture texture, SpineBundle old)
        {
            foreach (var viewer in LiveViewers().Where(item => item.getSvTexture() == texture))
            {
                viewerAtlas.SetValue(viewer, null);
                viewerData.SetValue(viewer, null);
                viewerContainer.SetValue(viewer, null);
                viewerTexture.SetValue(viewer, null);
                if (old != null && reserved.GetValue(viewer) is IDictionary cache) cache.Remove(old.Data);
            }
        }

        private static void ForgetReserved(BetobetoManager.SvTexture texture, SpineBundle old)
        {
            if (old == null) return;
            foreach (var viewer in LiveViewers().Where(item => item.getSvTexture() == texture))
                if (reserved.GetValue(viewer) is IDictionary cache) cache.Remove(old.Data);
        }

        private static void RefreshSpineViewers(ReplacementSelection previous, bool force)
        {
            foreach (var viewer in LiveViewers())
            {
                var texture = viewer.getSvTexture();
                if (texture == null || viewer.getBaseAnimName() == null) continue;
                string key = viewer.replace_json_key ?? texture.MtiText.default_json_key;
                string identity = SpineIdentity(texture.key, key);
                if ((!force && previous.SameSpine(selection, identity)
                    && (!spineStates.TryGetValue(texture, out var unchanged) || unchanged.Attempt == revision))
                    || (!HasActive(texture) && !HasLayers(identity))) continue;
                try
                {
                    if (!viewer.enabled)
                    {
                        // 隐藏姿态等下次显示时再准备，但撤销授权的旧资源立即释放。
                        if (spineStates.TryGetValue(texture, out var hidden) && hidden.Current != null
                            && !CanRetain(hidden.Current.Sources, identity) && InstallSpine(texture, hidden, key, null))
                        {
                            ReplayViewerAnimation(viewer);
                            hidden.Attempt = -1;
                        }
                        continue;
                    }
                    if (UpdateSpine(texture, key, viewer.getMaterial(), viewer)) ReplayViewerAnimation(viewer);
                }
                catch (Exception ex) { BLog.Error("Replacement refresh failed for one Spine viewer.", ex); }
            }
        }
    }
}
