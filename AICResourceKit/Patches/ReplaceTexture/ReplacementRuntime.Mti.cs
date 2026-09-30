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
    // 单图容器与直接 LoadImage 共用应用流程；MImage.Tx 同时更新游戏缓存的材质。
    internal static partial class ReplacementRuntime
    {
        private sealed class MtiRecord
        {
            internal MTI Container;
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
        private static readonly Dictionary<MTI, Dictionary<string, MtiRecord>> directMtiRecords =
            new Dictionary<MTI, Dictionary<string, MtiRecord>>();

        private static IEnumerable<MtiRecord> AllMtiRecords() => mtiRecords.Values
            .Concat(directMtiRecords.Values.SelectMany(records => records.Values));

        internal static void RegisterMtiImage(MTI container, string imageKey, MImage image)
        {
            if (!initialized || !MtiResourceAddress.UsesDirectImageEntry(container)
                || imageKey == null || image?.Tx == null) return;
            string assetKey = MtiResourceAddress.ContainerKey(container.resources_path);
            if (assetKey == null) return;
            if (!directMtiRecords.TryGetValue(container, out var records))
            {
                records = new Dictionary<string, MtiRecord>(StringComparer.Ordinal);
                directMtiRecords.Add(container, records);
            }
            if (!records.TryGetValue(imageKey, out var record))
            {
                record = new MtiRecord { Container = container, AssetKey = assetKey, ImageKey = imageKey, Image = image };
                records.Add(imageKey, record);
            }
            else if (!ReferenceEquals(record.Image, image))
            {
                Restore(record);
                record.Image = image;
                record.Original = null;
                record.Attempt = -1;
            }
            // 首次返回前应用候选；后续缓存命中不重复读取或创建纹理。
            ApplyMtiTexture(record, firstAccess: true);
        }

        internal static void ReleaseMti(MTI container)
        {
            if (container == null) return;
            ReleaseAtlasContainer(container);
            if (container is MTIOneImage single && mtiRecords.TryGetValue(single, out var primary))
            {
                mtiRecords.Remove(single);
                Restore(primary, refreshUsers: false);
            }
            if (directMtiRecords.TryGetValue(container, out var records))
            {
                directMtiRecords.Remove(container);
                foreach (var record in records.Values) Restore(record, refreshUsers: false);
            }
        }

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
            foreach (var record in AllMtiRecords().ToArray()) ApplyMtiTexture(record);
        }

        private static void ApplyMtiTexture(MtiRecord record, bool firstAccess = false)
        {
            if (selectionDelay.Waiting && !firstAccess) return;
            if (record.Attempt == revision && record.Image != null && record.Pending == null) return;
            var image = record.Container is MTIOneImage ? mtiImage.GetValue(record.Container) as MImage : record.Image;
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
                byte[] bytes;
                if (firstAccess && record.Replacement == null)
                {
                    record.Pending?.Dispose();
                    record.Pending = null;
                    bytes = ReplacementPreparation.Texture(layer, PatchInfo.ReplaceImagePath,
                        PatchInfo.ReplaceSensitiveImagePath, selection.AllowSensitive, default(System.Threading.CancellationToken));
                }
                else
                {
                    if (record.Pending == null)
                    {
                        record.Pending = PrepareTexture(layer);
                        ReplacementDiagnosticRuntime.Mti(record.AssetKey, record.ImageKey, "candidate-pending", "preparing", layer.PackageId);
                        record.Attempt = revision;
                    }
                    if (!record.Pending.IsCompleted || !ClaimUpload()) return;
                    record.Pending.TryTake(out bytes, out var error);
                    record.Pending = null;
                    if (error != null) throw error;
                }
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

        private static void Restore(MtiRecord record, bool refreshUsers = true)
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
                if (refreshUsers) RefreshMtiUsers(record);
                ReplacementDiagnosticRuntime.Mti(record.AssetKey, record.ImageKey, "restored",
                    record.Original == null ? "replacement-disposed-original-unavailable" : "original-restored");
            }
        }

        private static void InvalidateMtiSelection(ReplacementSelection previous, bool force)
        {
            foreach (var record in AllMtiRecords())
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
