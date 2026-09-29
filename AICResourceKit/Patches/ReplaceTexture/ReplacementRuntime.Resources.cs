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
            ApplyResource(record, firstAccess: true);
            return record.Replacement ?? original;
        }

        private static void RefreshResourceRecords()
        {
            foreach (var record in resourceRecords.Values.ToArray()) ApplyResource(record);
        }

        private static void ApplyResource(ResourceRecord record, bool firstAccess = false)
        {
            if (selectionDelay.Waiting && !firstAccess) return;
            if (record.Attempt == revision && record.Pending == null) return;
            if (record.Original == null) { DisposeResource(record); record.Attempt = revision; return; }
            var layer = Enabled ? selection.Texture("resources", record.Path, null, record.ObjectType) : null;
            if (HasInvalidTextureLayer("resources", record.Path, null, record.ObjectType)
                || (record.Source != null && HasUnidentifiedErrors(new[] { record.Source })))
            {
                record.Pending?.Dispose();
                record.Pending = null;
                BLog.Warn("Resources replacement target is damaged: " + record.Path + " (" + record.ObjectType + ").");
                ReplacementDiagnosticRuntime.Resource(record.Path, record.ObjectType, "candidate-failed", "invalid-layer", "Invalid or unidentified manifest target; see catalog errors.");
                if (record.Replacement == null || !CanRetain(new[] { record.Source }, record.SourceIdentity))
                    DisposeResource(record);
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
                DisposeResource(record);
                record.Attempt = revision;
                return;
            }
            try
            {
                if (record.Replacement != null && !CanRetain(new[] { record.Source }, record.SourceIdentity)) DisposeResource(record);
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
                Texture source = record.Original is Sprite sprite ? sprite.texture : (Texture)record.Original;
                record.Texture = LoadStableTexture(bytes, source, record.Texture);
                if (record.ObjectType == "Texture2D") record.Replacement = record.Texture;
                else if (!(record.Replacement is Sprite)) record.Replacement = CreateSprite((Sprite)record.Original, record.Texture);
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
                if (record.Replacement == null || !CanRetain(new[] { record.Source }, record.SourceIdentity)) DisposeResource(record);
                record.Attempt = revision;
            }
        }

        private static Sprite CreateSprite(Sprite original, Texture2D texture)
        {
            Rect rect = original.rect;
            Vector2 pivot = new Vector2(original.pivot.x / rect.width, original.pivot.y / rect.height);
            var sprite = Sprite.Create(texture, rect, pivot, original.pixelsPerUnit, 0,
                SpriteMeshType.FullRect, original.border);
            sprite.name = original.name;
            sprite.hideFlags = original.hideFlags;
            return sprite;
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
