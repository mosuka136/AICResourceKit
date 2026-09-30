using AICResourceKit.BLogSpace;
using AICResourceKit.Contracts;
using HarmonyLib;
using PixelLiner;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AICResourceKit.Patches.ReplaceTexture
{
    internal static partial class ReplacementRuntime
    {
        private sealed class PxlCharacterRecord
        {
            internal PxlSourceKey Source;
            internal int AuditRevision = -1;
            internal readonly Dictionary<string, PxlBinding> Bindings = new Dictionary<string, PxlBinding>(StringComparer.Ordinal);
        }

        private sealed class PxlBinding
        {
            internal PxlResourceAddress Address;
            internal PxlsTexture Slot;
            internal PxlSurface Surface;
        }

        private sealed class PxlSurface
        {
            internal Texture Texture;
            internal readonly HashSet<PxlBinding> Bindings = new HashSet<PxlBinding>();
            internal SharedTextureContents Contents;
            internal ReplacementTarget Applied;
            internal ReplacementWork<byte[]> Pending;
            internal int Attempt = -1;
        }

        private static readonly FieldInfo pxlI = AccessTools.Field(typeof(PxlImage), "I");
        private static readonly FieldInfo pxlP = AccessTools.Field(typeof(PxlImage), "P");
        private static readonly FieldInfo pxlAtlasTexture = AccessTools.Field(typeof(PxlsImgAtlas), "Image");
        private static readonly Dictionary<PxlCharacter, PxlCharacterRecord> pxlCharacters = new Dictionary<PxlCharacter, PxlCharacterRecord>();
        private static readonly Dictionary<PxlsTexture, HashSet<PxlBinding>> pxlSlots = new Dictionary<PxlsTexture, HashSet<PxlBinding>>();
        private static readonly Dictionary<Texture, PxlSurface> pxlSurfaces = new Dictionary<Texture, PxlSurface>();

        internal static void GuardPxl(Action action)
        {
            if (!initialized) return;
            try { action(); }
            catch (Exception ex) { BLog.Error("PXL replacement hook failed.", ex); }
        }

        internal static void RegisterPxlSource(PxlCharacter character, PxlSourceKey source)
        {
            if (source == null) return;
            if (!pxlCharacters.TryGetValue(character, out var record))
                pxlCharacters.Add(character, record = new PxlCharacterRecord());
            record.Source = source;
        }

        internal static void RegisterPxlImage(PxlImage image)
        {
            if (image.pChar == null || !pxlCharacters.TryGetValue(image.pChar, out var record)) return;
            var source = record.Source;
            foreach (var field in new[] { pxlI, pxlP })
            {
                var texture = field.GetValue(image) as Texture;
                if (texture != null) BindPxl(record,
                    PxlResourceAddress.Embedded(source.AssetKey, source.TextKey, image.id, image.id2, field.Name), null, texture);
            }
        }

        internal static void SharePxlImages(PxlCharacter character)
        {
            var images = character.getImageObject();
            var owner = images?.Values.Select(image => image.pChar).FirstOrDefault(value => value != null && pxlCharacters.ContainsKey(value));
            if (owner == null || ReferenceEquals(owner, character)) return;
            var record = pxlCharacters[owner];
            RegisterPxlSource(character, record.Source);
            foreach (var binding in record.Bindings.Values.ToArray())
                BindPxl(pxlCharacters[character], binding.Address, binding.Slot, binding.Surface?.Texture);
        }

        internal static void RegisterPxlAtlas(PxlsImgAtlas atlas, int ordinal)
        {
            if (atlas.pChar == null || atlas.img_type < 0 || !pxlCharacters.TryGetValue(atlas.pChar, out var record)) return;
            var slot = pxlAtlasTexture.GetValue(atlas) as PxlsTexture;
            if (slot == null) return;
            BindPxl(record, PxlResourceAddress.Page(record.Source.AssetKey, record.Source.TextKey, ordinal, atlas.img_type), slot, slot.Image);
            RegisterPxlExternal(atlas.pChar);
        }

        internal static void RegisterPxlExternal(PxlCharacter character)
        {
            if (!pxlCharacters.TryGetValue(character, out var record)) return;
            var pages = character.getExternalTextureArray();
            if (pages == null) return;
            for (int index = 0; index < pages.Length; index++)
                if (pages[index] != null) BindPxl(record,
                    PxlResourceAddress.Page(record.Source.AssetKey, record.Source.TextKey, index), pages[index], pages[index].Image);
        }

        private static void BindPxl(PxlCharacterRecord record, PxlResourceAddress address, PxlsTexture slot, Texture texture)
        {
            if (!record.Bindings.TryGetValue(address.Identity, out var binding))
            {
                binding = new PxlBinding { Address = address };
                record.Bindings.Add(address.Identity, binding);
            }
            if (binding.Slot != slot)
            {
                RemovePxlSlot(binding);
                binding.Slot = slot;
                if (slot != null)
                {
                    if (!pxlSlots.TryGetValue(slot, out var entries)) pxlSlots.Add(slot, entries = new HashSet<PxlBinding>());
                    entries.Add(binding);
                }
            }
            AttachPxlSurface(binding, texture);
            DescribePxl(binding, "entry-hit", texture == null ? "page-awaiting-texture" : "image-registered");
        }

        private static void AttachPxlSurface(PxlBinding binding, Texture texture)
        {
            if (binding.Surface != null && ReferenceEquals(binding.Surface.Texture, texture)) return;
            DetachPxlSurface(binding);
            if (texture == null) return;
            if (!pxlSurfaces.TryGetValue(texture, out var surface))
                pxlSurfaces.Add(texture, surface = new PxlSurface { Texture = texture });
            surface.Bindings.Add(binding);
            binding.Surface = surface;
            surface.Attempt = -1;
            ApplyPxlSurface(surface, true);
        }

        internal static void RebindPxlTexture(PxlsTexture slot)
        {
            if (pxlSlots.TryGetValue(slot, out var bindings))
                foreach (var binding in bindings.ToArray()) AttachPxlSurface(binding, slot.Image);
        }

        internal static void UnbindPxlTexture(PxlsTexture slot)
        {
            if (pxlSlots.TryGetValue(slot, out var bindings))
                foreach (var binding in bindings.ToArray()) DetachPxlSurface(binding);
        }

        private static void DetachPxlSurface(PxlBinding binding)
        {
            var surface = binding.Surface;
            if (surface == null) return;
            binding.Surface = null;
            surface.Bindings.Remove(binding);
            if (surface.Bindings.Count == 0)
            {
                RestorePxlSurface(surface);
                pxlSurfaces.Remove(surface.Texture);
            }
            else
            {
                surface.Pending?.Dispose();
                surface.Pending = null;
                surface.Attempt = -1;
            }
        }

        private static void RemovePxlSlot(PxlBinding binding)
        {
            if (binding.Slot != null && pxlSlots.TryGetValue(binding.Slot, out var entries))
            {
                entries.Remove(binding);
                if (entries.Count == 0) pxlSlots.Remove(binding.Slot);
            }
            binding.Slot = null;
        }

        internal static void ReleasePxl(PxlCharacter character)
        {
            if (!pxlCharacters.TryGetValue(character, out var record)) return;
            pxlCharacters.Remove(character);
            foreach (var binding in record.Bindings.Values)
            {
                DescribePxl(binding, "released", "character-released");
                DetachPxlSurface(binding);
                RemovePxlSlot(binding);
            }
        }

        private static void PumpPxlSurfaces()
        {
            foreach (var surface in pxlSurfaces.Values.ToArray()) ApplyPxlSurface(surface, false);
            foreach (var pair in pxlCharacters)
            {
                var record = pair.Value;
                if (!pair.Key.isLoadCompleted() || record.AuditRevision == revision) continue;
                record.AuditRevision = revision;
                foreach (var target in selection.PxlTargets(record.Source.AssetKey, record.Source.TextKey))
                    if (!record.Bindings.ContainsKey(target.Identity))
                    {
                        ReplacementDiagnosticRuntime.Record(ReplacementDiagnosticTarget.Pxl(target.PxlAddress),
                            "candidate-failed", "PXL image/page / ReplacementRuntime", "address-not-found",
                            "The decoded PXL does not contain this image role/page address.");
                        BLog.Warn("PXL target address not found: " + PortraitJson.Serialize(target.PxlAddress.Describe()));
                    }
            }
        }

        private static void InvalidatePxlSelection()
        {
            foreach (var surface in pxlSurfaces.Values)
            {
                surface.Pending?.Dispose();
                surface.Pending = null;
                surface.Attempt = -1;
            }
        }

        private static void ApplyPxlSurface(PxlSurface surface, bool firstAccess)
        {
            if (surface.Texture == null)
            {
                RestorePxlSurface(surface);
                pxlSurfaces.Remove(surface.Texture);
                foreach (var binding in surface.Bindings) binding.Surface = null;
                return;
            }
            if (surface.Attempt == revision && surface.Pending == null) return;
            try
            {
                var identities = new HashSet<string>(surface.Bindings.Select(binding => binding.Address.Identity), StringComparer.Ordinal);
                if (identities.Any(selection.Invalid)) throw new InvalidOperationException("Invalid PXL target; see catalog errors.");
                var target = Enabled ? selection.Pxl(identities) : null;
                if (target == null)
                {
                    RestorePxlSurface(surface);
                    surface.Attempt = revision;
                    return;
                }
                if (AllMtiRecords().Any(record =>
                    (ReferenceEquals(record.Original, surface.Texture) || ReferenceEquals(record.Replacement, surface.Texture)
                        || (record.Container is XX.MTIOneImage single && ReferenceEquals(single.Image, surface.Texture)))
                    && selection.Texture("mti", record.AssetKey, record.ImageKey, null) != null))
                    throw new InvalidOperationException("The same PXL texture is also targeted by a legacy MTI pack; enable only one addressing route.");
                byte[] bytes;
                if (firstAccess)
                {
                    surface.Pending?.Dispose();
                    surface.Pending = null;
                    bytes = ReplacementPreparation.Texture(target, PatchInfo.ReplaceImagePath,
                        PatchInfo.ReplaceSensitiveImagePath, selection.AllowSensitive, default(System.Threading.CancellationToken));
                }
                else
                {
                    if (surface.Pending == null)
                    {
                        // 撤销授权时立即恢复，不等待新文件准备。
                        if (surface.Applied != null && !CanRetain(new[] { surface.Applied.Owner }, surface.Applied.Identity))
                            RestorePxlSurface(surface);
                        surface.Pending = PrepareTexture(target);
                        DescribePxl(surface, "candidate-pending", "preparing", target.PackageId);
                    }
                    if (!surface.Pending.IsCompleted || !ClaimUpload()) return;
                    surface.Pending.TryTake(out bytes, out var error);
                    surface.Pending = null;
                    if (error != null) throw error;
                }
                ValidateCurrent(target);
                if (surface.Contents == null) surface.Contents = new SharedTextureContents(surface.Texture);
                surface.Contents.Apply(bytes);
                surface.Applied = target;
                surface.Attempt = revision;
                DescribePxl(surface, "candidate-applied", "shared-texture-updated", target.PackageId);
            }
            catch (Exception ex)
            {
                RestorePxlSurface(surface);
                surface.Attempt = revision;
                DescribePxl(surface, "candidate-failed", ex.GetType().Name, ex.Message);
                BLog.Error("PXL image/page replacement rejected.", ex);
            }
        }

        private static void RestorePxlSurface(PxlSurface surface)
        {
            surface.Pending?.Dispose();
            surface.Pending = null;
            if (surface.Contents != null)
            {
                try { surface.Contents.Restore(); }
                finally { surface.Contents.Dispose(); surface.Contents = null; }
            }
            if (surface.Applied != null) DescribePxl(surface, "restored", "original-pixels-restored");
            surface.Applied = null;
        }

        private static void DescribePxl(PxlSurface surface, string stage, string outcome, string reason = null)
        {
            foreach (var binding in surface.Bindings) DescribePxl(binding, stage, outcome, reason);
        }

        private static void DescribePxl(PxlBinding binding, string stage, string outcome, string reason = null)
        {
            if (!ReplacementDiagnosticRuntime.Enabled) return;
            var texture = binding.Surface?.Texture;
            ReplacementDiagnosticRuntime.Record(ReplacementDiagnosticTarget.Pxl(binding.Address), stage,
                "PXL image/page / ReplacementRuntime", outcome, reason, new Dictionary<string, object>
                {
                    ["address"] = binding.Address.Describe(), ["width"] = texture == null ? 0 : texture.width,
                    ["height"] = texture == null ? 0 : texture.height,
                    ["sharedAddresses"] = binding.Surface?.Bindings.Select(entry => entry.Address.Identity).Distinct().Count() ?? 0
                });
        }

        private static void DiscoverPxlBindings()
        {
            foreach (var character in pxlCharacters.Values)
                foreach (var binding in character.Bindings.Values) DescribePxl(binding, "discovered", "registered-image-or-page");
        }
    }
}
