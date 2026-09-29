using AICResourceKit.BLogSpace;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using XX;
using Object = UnityEngine.Object;

namespace AICResourceKit.Patches.ReplaceTexture
{
    // 跟踪 MTIOneImage 容器，更新纹理并通知已有立绘消费者。
    internal static partial class ReplacementRuntime
    {
        private sealed class MtiRecord
        {
            internal MTIOneImage Container;
            internal string AssetKey;
            internal string ImageKey;
            internal MImage Image;
            internal Texture Original;
            internal Texture2D Replacement;
            internal ReplacementPackage Source;
            internal string SourceIdentity;
            internal int Attempt = -1;
            internal ReplacementWork<byte[]> Pending;
        }

        private static readonly Dictionary<MTIOneImage, MtiRecord> mtiRecords = new Dictionary<MTIOneImage, MtiRecord>();
        private static readonly FieldInfo mtiImage = AccessTools.Field(typeof(MTIOneImage), "LImage_");

        private static void RefreshMtiSpineTextures(ReplacementSelection previous, bool force)
        {
            foreach (var texture in LiveViewers().Select(viewer => viewer.getSvTexture())
                .Where(texture => texture != null && !HasActive(texture)).Distinct())
            {
                if (!mtiRecords.TryGetValue(texture.MtiImage0, out var record) || record.Image == null) continue;
                if (!force && previous.SameTexture(selection, "mti", record.AssetKey, record.ImageKey, null)) continue;
                try { texture.cleanExecute(); }
                catch (Exception ex) { BLog.Error("Failed to refresh an MTI-backed Spine texture: " + texture.key, ex); }
            }
        }

        internal static void RegisterMti(MTIOneImage container, string assetKey, string imageKey)
        {
            if (!initialized || container == null || string.IsNullOrEmpty(assetKey) || mtiImage == null) return;
            if (!mtiRecords.TryGetValue(container, out var record))
            {
                record = new MtiRecord { Container = container };
                mtiRecords.Add(container, record);
            }
            record.AssetKey = assetKey;
            record.ImageKey = imageKey;
            ApplyMtiTexture(record);
        }

        private static void RetryMtiRecords()
        {
            foreach (var record in mtiRecords.Values.ToArray()) ApplyMtiTexture(record);
        }

        private static void ApplyMtiTexture(MtiRecord record)
        {
            if (selectionDelay.Waiting) return;
            if (record.Attempt == revision && record.Image != null && record.Pending == null) return;
            var image = mtiImage.GetValue(record.Container) as MImage;
            if (image == null || image.Tx == null) return;
            record.Image = image;
            if (record.Original == null) record.Original = image.Tx;
            var layer = Enabled ? selection.Texture("mti", record.AssetKey, record.ImageKey, null) : null;
            if (HasInvalidTextureLayer("mti", record.AssetKey, record.ImageKey, null)
                || (record.Source != null && HasUnidentifiedErrors(new[] { record.Source })))
            {
                record.Pending?.Dispose();
                record.Pending = null;
                BLog.Warn("MTI replacement target is damaged: " + record.AssetKey + ".");
                ReplacementDiagnosticRuntime.Mti(record.AssetKey, record.ImageKey, "candidate-failed", "invalid-layer", "Invalid or unidentified manifest target; see catalog errors.");
                if (record.Replacement == null || !CanRetain(new[] { record.Source }, record.SourceIdentity)) Restore(record);
                record.Attempt = revision;
                return;
            }
            if (layer == null)
            {
                record.Pending?.Dispose();
                record.Pending = null;
                if (record.Replacement != null && CanRetain(new[] { record.Source }, record.SourceIdentity))
                {
                    record.Attempt = revision;
                    return;
                }
                Restore(record);
                record.Attempt = revision;
                return;
            }
            try
            {
                if (record.Replacement != null && !CanRetain(new[] { record.Source }, record.SourceIdentity)) Restore(record);
                if (record.Pending == null)
                {
                    record.Pending = PrepareTexture(layer);
                    ReplacementDiagnosticRuntime.Mti(record.AssetKey, record.ImageKey, "candidate-pending", "preparing", layer.PackageId);
                    record.Attempt = revision;
                }
                if (!record.Pending.IsCompleted || !ClaimUpload()) return;
                record.Pending.TryTake(out var bytes, out var error);
                record.Pending = null;
                if (error != null) throw error;
                ValidateCurrent(layer);
                record.Replacement = LoadStableTexture(bytes, record.Original, record.Replacement);
                record.Source = layer.Owner;
                record.SourceIdentity = layer.Identity;
                record.Image.Tx = record.Replacement;
                record.Attempt = revision;
                RefreshMtiUsers(record);
                ReplacementDiagnosticRuntime.Mti(record.AssetKey, record.ImageKey, "candidate-applied", "mimage-texture-assigned", layer.PackageId);
            }
            catch (Exception ex)
            {
                record.Pending?.Dispose();
                record.Pending = null;
                BLog.Error("MTI replacement rejected: " + record.AssetKey, ex);
                ReplacementDiagnosticRuntime.Mti(record.AssetKey, record.ImageKey, "candidate-failed", ex.GetType().Name, ex.Message);
                if (record.Replacement == null || !CanRetain(new[] { record.Source }, record.SourceIdentity)) Restore(record);
                record.Attempt = revision;
            }
        }

        private static void RefreshMtiUsers(MtiRecord record)
        {
            foreach (var texture in LiveViewers().Select(viewer => viewer.getSvTexture())
                .Where(texture => texture != null && texture.MtiImage0 == record.Container && !HasActive(texture)).Distinct())
            {
                try { texture.cleanExecute(); displayRevision++; }
                catch (Exception ex) { BLog.Error("Failed to refresh an MTI-backed Spine texture: " + texture.key, ex); }
            }
        }

        private static void Restore(MtiRecord record)
        {
            record.Pending?.Dispose();
            record.Pending = null;
            bool changed = record.Replacement != null;
            if (record.Image != null && record.Original != null) record.Image.Tx = record.Original;
            if (record.Replacement != null) Object.Destroy(record.Replacement);
            record.Replacement = null;
            record.Source = null;
            record.SourceIdentity = null;
            if (changed)
            {
                RefreshMtiUsers(record);
                ReplacementDiagnosticRuntime.Mti(record.AssetKey, record.ImageKey, "restored",
                    record.Original == null ? "replacement-disposed-original-unavailable" : "original-restored");
            }
        }

        private static void InvalidateMtiSelection(ReplacementSelection previous, bool force)
        {
            foreach (var record in mtiRecords.Values)
            {
                if (!force && previous.SameTexture(selection, "mti", record.AssetKey, record.ImageKey, null)
                    && (record.Source == null || CanRetain(new[] { record.Source }, record.SourceIdentity))) record.Attempt = revision;
                else
                {
                    record.Pending?.Dispose();
                    record.Pending = null;
                    record.Attempt = -1;
                }
            }
        }
    }
}
