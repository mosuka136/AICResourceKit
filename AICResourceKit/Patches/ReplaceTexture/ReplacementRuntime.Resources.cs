using AICResourceKit.BLogSpace;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AICResourceKit.Patches.ReplaceTexture
{
    // 处理 Resources.Load 的首载与后续纹理刷新。
    internal static partial class ReplacementRuntime
    {
        private sealed class ResourceRecord
        {
            internal string Path;
            internal string ObjectType;
            internal Object Original;
            internal Texture2D Texture;
            internal Object Replacement;
            internal ReplacementPackage Source;
            internal string SourceIdentity;
            internal int Attempt = -1;
            internal ReplacementWork<byte[]> Pending;
        }

        private static readonly Dictionary<string, ResourceRecord> resourceRecords =
            new Dictionary<string, ResourceRecord>(StringComparer.Ordinal);

        internal static Object ReplaceResource(string path, Type requestedType, Object original)
        {
            if (!initialized || original == null || string.IsNullOrEmpty(path)) return original;
            string objectType = requestedType == typeof(Sprite) || original is Sprite ? "Sprite"
                : requestedType == typeof(Texture2D) || original is Texture2D ? "Texture2D" : null;
            if (objectType == null) return original;
            string key = path + "\n" + objectType;
            if (!resourceRecords.TryGetValue(key, out var record))
            {
                record = new ResourceRecord { Path = path, ObjectType = objectType, Original = original };
                resourceRecords.Add(key, record);
            }
            // Load(string) 会调用 Load(string, Type)，外层收到的可能已是内层返回的替换对象。
            if (ReferenceEquals(record.Replacement, original)) return original;
            if (record.Original != original)
            {
                DisposeResource(record);
                record.Original = original;
                record.Attempt = -1;
            }
            ApplyResource(record, firstAccess: true);
            return record.Replacement ?? original;
        }

        private static void RefreshResourceRecords()
        {
            foreach (var pair in resourceRecords.ToArray())
            {
                if (pair.Value.Original == null)
                {
                    DisposeResource(pair.Value);
                    resourceRecords.Remove(pair.Key);
                }
                else ApplyResource(pair.Value);
            }
        }

        internal static Object ReleaseResource(Object resource)
        {
            if (resource == null) return resource;
            // 调用方通常持有 Load 返回的替换对象；将卸载请求交回相应的原始资源。
            var owned = resourceRecords.Values.FirstOrDefault(record =>
                ReferenceEquals(record.Replacement, resource) || ReferenceEquals(record.Texture, resource));
            if (owned != null)
                resource = owned.Original != null && resource is Texture && owned.Original is Sprite originalSprite
                    ? originalSprite.texture : owned.Original;
            foreach (var pair in resourceRecords.ToArray())
            {
                var record = pair.Value;
                bool ownsTexture = record.Original != null && record.Original is Sprite sprite && sprite.texture == resource;
                if (record.Original != resource && !ownsTexture) continue;
                resourceRecords.Remove(pair.Key);
                DisposeResource(record);
            }
            return resource;
        }

        private static void ApplyResource(ResourceRecord record, bool firstAccess = false)
        {
            if (selectionDelay.Waiting && !firstAccess) return;
            if (record.Attempt == revision && record.Pending == null) return;
            if (record.Original == null) { DisposeResource(record); record.Attempt = revision; return; }
            var layer = Enabled ? selection.Texture("resources", record.Path, null, record.ObjectType) : null;
            if (HasInvalidTextureLayer("resources", record.Path, null, record.ObjectType)
                || (record.Source != null && HasUnidentifiedErrors(new[] { record.Source }, record.SourceIdentity)))
            {
                record.Pending?.Dispose();
                record.Pending = null;
                BLog.Warn("Resources replacement target is damaged: " + record.Path + " (" + record.ObjectType + ").");
                ReplacementDiagnosticRuntime.Resource(record.Path, record.ObjectType, "candidate-failed", "invalid-layer", "Invalid or unidentified manifest target; see catalog errors.");
                if (record.Replacement == null || !CanRetain(new[] { record.Source }, record.SourceIdentity))
                    RestoreResource(record);
                record.Attempt = revision;
                return;
            }
            if (layer == null)
            {
                record.Pending?.Dispose();
                record.Pending = null;
                if (record.Source != null && CanRetain(new[] { record.Source }, record.SourceIdentity))
                {
                    record.Attempt = revision;
                    return;
                }
                RestoreResource(record);
                record.Attempt = revision;
                return;
            }
            try
            {
                if (record.Source != null && !CanRetain(new[] { record.Source }, record.SourceIdentity)) RestoreResource(record);
                if (scan != null && !firstAccess) return;
                byte[] bytes;
                if (firstAccess && record.Replacement == null)
                {
                    // Resources.Load 的首次返回值会被调用方永久持有，不能先返回原对象再偷偷换引用。
                    // 这个入口保持同步首载；已有替换对象的刷新继续在后台准备并原位更新。
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
                        ReplacementDiagnosticRuntime.Resource(record.Path, record.ObjectType, "candidate-pending", "preparing", layer.PackageId);
                        record.Attempt = revision;
                    }
                    if (!record.Pending.IsCompleted || !ClaimUpload()) return;
                    record.Pending.TryTake(out bytes, out var error);
                    record.Pending = null;
                    if (error != null) throw error;
                }
                ValidateCurrent(layer);
                var originalSprite = record.Original as Sprite;
                var spriteLayout = originalSprite != null && record.Replacement == null
                    ? ReplacementSpriteLayout.Read(originalSprite) : null;
                Texture source = originalSprite != null ? originalSprite.texture : (Texture)record.Original;
                record.Texture = LoadStableTexture(bytes, source, record.Texture);
                if (record.ObjectType == "Texture2D") record.Replacement = record.Texture;
                else if (spriteLayout != null)
                {
                    var sprite = spriteLayout.Create(record.Texture);
                    sprite.name = originalSprite.name;
                    sprite.hideFlags = originalSprite.hideFlags;
                    record.Replacement = sprite;
                }
                record.Source = layer.Owner;
                record.SourceIdentity = layer.Identity;
                record.Attempt = revision;
                ReplacementDiagnosticRuntime.Resource(record.Path, record.ObjectType, "candidate-applied", "replacement-object-ready", layer.PackageId);
            }
            catch (Exception ex)
            {
                record.Pending?.Dispose();
                record.Pending = null;
                BLog.Error("Resources replacement rejected: " + record.Path + " (" + record.ObjectType + ")", ex);
                ReplacementDiagnosticRuntime.Resource(record.Path, record.ObjectType, "candidate-failed", ex.GetType().Name, ex.Message);
                if (record.Source == null || !CanRetain(new[] { record.Source }, record.SourceIdentity)) RestoreResource(record);
                record.Attempt = revision;
            }
        }

        private static void RestoreResource(ResourceRecord record)
        {
            record.Pending?.Dispose();
            record.Pending = null;
            if (record.Replacement == null || record.Original == null)
            {
                DisposeResource(record);
                return;
            }
            if (record.Source == null) return;
            try
            {
                Texture source = record.Original is Sprite sprite ? sprite.texture : (Texture)record.Original;
                RestoreTextureContents(source, record.Texture);
                record.Source = null;
                record.SourceIdentity = null;
                ReplacementDiagnosticRuntime.Resource(record.Path, record.ObjectType, "restored", "stable-object-original-content");
            }
            catch (Exception ex)
            {
                BLog.Error("Could not restore the original Resources texture: " + record.Path, ex);
                DisposeResource(record);
            }
        }

        private static void DisposeResource(ResourceRecord record)
        {
            bool changed = record.Replacement != null;
            record.Pending?.Dispose();
            record.Pending = null;
            if (record.Replacement is Sprite sprite) Object.Destroy(sprite);
            if (record.Texture != null) Object.Destroy(record.Texture);
            record.Replacement = null;
            record.Texture = null;
            record.Source = null;
            record.SourceIdentity = null;
            if (changed) ReplacementDiagnosticRuntime.Resource(record.Path, record.ObjectType, "released", "replacement-disposed");
        }

        private static void InvalidateResourceSelection(ReplacementSelection previous, bool force)
        {
            foreach (var record in resourceRecords.Values)
            {
                if (!force && previous.SameTexture(selection, "resources", record.Path, null, record.ObjectType)
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
