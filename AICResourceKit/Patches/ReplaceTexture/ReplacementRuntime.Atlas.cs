using AICResourceKit.BLogSpace;
using AICResourceKit.Contracts;
using Spine;
using Spine.Unity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using XX;

namespace AICResourceKit.Patches.ReplaceTexture
{
    internal static partial class ReplacementRuntime
    {
        private sealed class AtlasSource
        {
            internal string Loader, AssetKey, AtlasKey;
            internal AtlasResourceAddress Address(string member, bool page = false) => AtlasResourceAddress.Create(Loader, AssetKey, AtlasKey, member, page);
        }

        private sealed class AtlasBinding
        {
            internal SpineAtlasAsset Asset;
            internal AtlasSource Source;
            internal Atlas Metadata;
            internal readonly Dictionary<string, AtlasPageBinding> Pages = new Dictionary<string, AtlasPageBinding>(StringComparer.Ordinal);
            internal int Audit = -1;
        }

        private sealed class AtlasPageBinding
        {
            internal AtlasBinding Atlas;
            internal AtlasPage Page;
            internal AtlasSurface Surface;
        }

        private sealed class AtlasSurface
        {
            internal Texture2D Texture;
            internal readonly HashSet<AtlasPageBinding> Bindings = new HashSet<AtlasPageBinding>();
            internal SharedTextureContents Contents;
            internal ReplacementWork<AtlasPixels[]> Pending;
            internal List<AtlasEdit> Applied;
            internal int Attempt = -1;
        }

        private sealed class AtlasEdit
        {
            internal ReplacementTarget Target;
            internal AtlasEditLayout Layout;
        }

        private sealed class AtlasPixels
        {
            internal AtlasEdit Edit;
            internal byte[] Bytes;
        }

        private static readonly ConditionalWeakTable<SpineAtlasAsset, AtlasSource> atlasSources = new ConditionalWeakTable<SpineAtlasAsset, AtlasSource>();
        private static readonly ConditionalWeakTable<TextAsset, AtlasSource> resourceAtlasTexts = new ConditionalWeakTable<TextAsset, AtlasSource>();
        private static readonly Dictionary<SpineAtlasAsset, AtlasBinding> atlasBindings = new Dictionary<SpineAtlasAsset, AtlasBinding>();
        private static readonly Dictionary<Texture2D, AtlasSurface> atlasSurfaces = new Dictionary<Texture2D, AtlasSurface>();

        internal static void RememberAtlasResourceText(TextAsset text, string path)
        {
            resourceAtlasTexts.Remove(text);
            resourceAtlasTexts.Add(text, new AtlasSource { Loader = "resources", AtlasKey = path });
        }

        internal static void RememberAtlasSource(SpineAtlasAsset asset, string loader, string assetKey, string atlasKey)
        {
            if (asset == null) return;
            atlasSources.Remove(asset);
            atlasSources.Add(asset, new AtlasSource { Loader = loader, AssetKey = assetKey, AtlasKey = atlasKey });
        }

        internal static void RegisterAtlas(SpineAtlasAsset asset, Atlas loaded, Texture2D singleTexture = null)
        {
            if (asset == null || asset.atlasFile == null || (loaded == null && singleTexture == null)) return;
            if (!atlasSources.TryGetValue(asset, out var source))
            {
                if (!resourceAtlasTexts.TryGetValue(asset.atlasFile, out source))
                {
                    var textSource = ReplacementPxlSource.Find(asset.atlasFile);
                    if (textSource == null) return;
                    source = new AtlasSource { Loader = "mti", AssetKey = textSource.AssetKey, AtlasKey = textSource.TextKey };
                }
                RememberAtlasSource(asset, source.Loader, source.AssetKey, source.AtlasKey);
                atlasSources.TryGetValue(asset, out source);
            }
            if (!atlasBindings.TryGetValue(asset, out var binding))
            {
                binding = new AtlasBinding { Asset = asset, Source = source, Metadata = PortraitCatalog.ReadAtlas(asset.atlasFile.text) };
                atlasBindings.Add(asset, binding);
            }
            foreach (var page in binding.Metadata.Pages)
            {
                var image = singleTexture != null && binding.Metadata.Pages.Count == 1 ? singleTexture
                    : (loaded?.Pages.FirstOrDefault(item => item.name == page.name)?.rendererObject as Material)?.mainTexture as Texture2D;
                if (image == null) continue; // 主立绘的污渍 RenderTexture 保留原有 SvTexture 路径。
                if (!binding.Pages.TryGetValue(page.name, out var entry))
                {
                    entry = new AtlasPageBinding { Atlas = binding, Page = page };
                    binding.Pages.Add(page.name, entry);
                }
                if (entry.Surface != null && entry.Surface.Texture == image) continue;
                DetachAtlasPage(entry);
                if (!atlasSurfaces.TryGetValue(image, out var surface))
                    atlasSurfaces.Add(image, surface = new AtlasSurface { Texture = image });
                entry.Surface = surface; surface.Bindings.Add(entry); surface.Attempt = -1;
                ApplyAtlasSurface(surface, true);
                DescribeAtlasBinding(entry);
            }
        }

        internal static void DescribeAtlasRegion(SpineAtlasAsset asset, string region)
        {
            if (asset == null || region == null || !atlasBindings.TryGetValue(asset, out var binding)) return;
            var found = binding.Metadata.FindRegion(region);
            RecordAtlas(binding.Source.Address(region), "entry-hit", found == null ? "region-not-found" : "picture-region-bound",
                found == null ? "Atlas region was not found." : null, found?.page.name);
        }

        internal static void ReleaseAtlas(SpineAtlasAsset asset)
        {
            if (!atlasBindings.TryGetValue(asset, out var binding)) return;
            atlasBindings.Remove(asset);
            foreach (var page in binding.Pages.Values) DetachAtlasPage(page);
        }

        private static void ReleaseAtlasContainer(MTI container)
        {
            string key = MtiResourceAddress.ContainerKey(container.resources_path);
            var textures = new HashSet<Texture>(AllMtiRecords().Where(record => ReferenceEquals(record.Container, container))
                .SelectMany(record => new[] { record.Original, record.Replacement, record.Image?.Tx }).Where(texture => texture != null));
            foreach (var binding in atlasBindings.Values.ToArray())
                if ((binding.Source.Loader == "mti" && binding.Source.AssetKey == key)
                    || binding.Pages.Values.Any(page => page.Surface != null && textures.Contains(page.Surface.Texture))) ReleaseAtlas(binding.Asset);
        }

        private static void DetachAtlasPage(AtlasPageBinding page)
        {
            var surface = page.Surface;
            if (surface == null) return;
            page.Surface = null; surface.Bindings.Remove(page);
            if (surface.Bindings.Count == 0)
            {
                RestoreAtlasSurface(surface);
                atlasSurfaces.Remove(surface.Texture);
            }
            else { surface.Pending?.Dispose(); surface.Pending = null; surface.Attempt = -1; }
        }

        private static void InvalidateAtlasSelection()
        {
            foreach (var surface in atlasSurfaces.Values)
            { surface.Pending?.Dispose(); surface.Pending = null; surface.Attempt = -1; }
        }

        private static void PumpAtlasSurfaces()
        {
            foreach (var binding in atlasBindings.Values.ToArray())
            {
                if (binding.Asset == null) { ReleaseAtlas(binding.Asset); continue; }
                if (binding.Audit == revision || binding.Metadata.Pages.Count == 0) continue;
                binding.Audit = revision;
                var address = binding.Source.Address(binding.Metadata.Pages[0].name, true);
                foreach (var target in selection.AtlasTargets(address))
                {
                    try { AtlasEditLayout.Resolve(binding.Metadata, target.AtlasAddress); }
                    catch (Exception ex) { RecordAtlas(target.AtlasAddress, "candidate-failed", "invalid-address-or-layout", ex.Message); }
                }
            }
            foreach (var surface in atlasSurfaces.Values.ToArray())
            {
                if (surface.Texture == null)
                {
                    foreach (var page in surface.Bindings.ToArray()) DetachAtlasPage(page);
                    continue;
                }
                ApplyAtlasSurface(surface, false);
            }
        }

        private static List<AtlasEdit> SelectAtlasEdits(AtlasSurface surface)
        {
            var result = new Dictionary<string, AtlasEdit>(StringComparer.Ordinal);
            foreach (var binding in surface.Bindings)
            {
                var address = binding.Atlas.Source.Address(binding.Page.name, true);
                foreach (var target in selection.AtlasTargets(address))
                {
                    // 同 atlas 的其他页不会因本页候选损坏而被替换或回退。
                    var region = target.AtlasAddress.IsPage ? null : binding.Atlas.Metadata.FindRegion(target.AtlasAddress.MemberKey);
                    string pageKey = target.AtlasAddress.IsPage ? target.AtlasAddress.MemberKey : region?.page.name;
                    if (pageKey != binding.Page.name) continue;
                    if (selection.Invalid(target.Identity)) throw new InvalidDataException("Invalid atlas target; see catalog errors.");
                    result[target.Identity] = new AtlasEdit { Target = target,
                        Layout = AtlasEditLayout.Resolve(binding.Atlas.Metadata, target.AtlasAddress) };
                }
            }
            var edits = result.Values.ToList();
            AtlasEditLayout.ValidateEdits(edits.Select(edit => edit.Layout).ToArray());
            return edits;
        }

        private static void ApplyAtlasSurface(AtlasSurface surface, bool firstAccess)
        {
            if (surface.Attempt == revision && surface.Pending == null) return;
            List<AtlasEdit> edits = null;
            try
            {
                edits = SelectAtlasEdits(surface);
                if (edits.Count == 0) { RestoreAtlasSurface(surface); surface.Attempt = revision; return; }
                if (edits.Any(edit => edit.Layout.Page.width != surface.Texture.width || edit.Layout.Page.height != surface.Texture.height))
                    throw new InvalidDataException("The bound texture does not match the original atlas page dimensions.");
                if (edits.Any(edit => selection.HasSpinePageOverride(edit.Target.AtlasAddress)))
                    throw new InvalidDataException("Atlas page/region edits conflict with a spine-assets image/pages/atlas override; enable one route.");
                if (AllMtiRecords().Any(record => (record.Original == surface.Texture || record.Replacement == surface.Texture || record.Image?.Tx == surface.Texture)
                    && selection.Texture("mti", record.AssetKey, record.ImageKey, null) != null))
                    throw new InvalidDataException("The atlas texture is also targeted by an MTI image pack; enable one addressing route.");
                if (surface.Applied != null && surface.Applied.Any(edit => !CanRetain(new[] { edit.Target.Owner }, edit.Target.Identity)))
                    RestoreAtlasSurface(surface);
                if (HasPxlEdits(surface.Texture))
                    throw new InvalidDataException("The same texture is targeted by both PXL and atlas packs; enable one addressing route.");
                if (scan != null && !firstAccess) return;
                AtlasPixels[] prepared;
                if (firstAccess)
                {
                    surface.Pending?.Dispose(); surface.Pending = null;
                    prepared = PrepareAtlasPixels(edits, selection.AllowSensitive, default(System.Threading.CancellationToken));
                }
                else
                {
                    if (surface.Pending == null)
                    {
                        bool allow = selection.AllowSensitive;
                        surface.Pending = new ReplacementWork<AtlasPixels[]>(token => PrepareAtlasPixels(edits, allow, token));
                        foreach (var edit in edits) RecordAtlas(edit.Target.AtlasAddress, "candidate-pending", "preparing", page: edit.Layout.Page.name);
                    }
                    if (!surface.Pending.IsCompleted || !ClaimUpload()) return;
                    surface.Pending.TryTake(out prepared, out var error); surface.Pending = null;
                    if (error != null) throw error;
                }
                foreach (var edit in edits) ValidateCurrent(edit.Target);
                if (surface.Contents == null) surface.Contents = new SharedTextureContents(surface.Texture);
                ApplyAtlasPixels(surface, prepared);
                surface.Applied = edits; surface.Attempt = revision;
                foreach (var edit in edits) RecordAtlas(edit.Target.AtlasAddress, "candidate-applied", "shared-atlas-texture-updated", page: edit.Layout.Page.name);
            }
            catch (Exception ex)
            {
                RestoreAtlasSurface(surface); surface.Attempt = revision;
                if (edits != null)
                    foreach (var edit in edits) RecordAtlas(edit.Target.AtlasAddress, "candidate-failed", "page-rejected", ex.Message, edit.Layout.Page.name);
                foreach (var binding in surface.Bindings)
                    RecordAtlas(binding.Atlas.Source.Address(binding.Page.name, true), "candidate-failed", "page-rejected", ex.Message);
                BLog.Error("Atlas texture edits rejected.", ex);
            }
        }

        private static AtlasPixels[] PrepareAtlasPixels(List<AtlasEdit> edits, bool allowSensitive, System.Threading.CancellationToken token)
        {
            var result = new List<AtlasPixels>();
            foreach (var edit in edits)
            {
                byte[] bytes = ReplacementPreparation.Texture(edit.Target, PatchInfo.ReplaceImagePath, PatchInfo.ReplaceSensitiveImagePath, allowSensitive, token);
                PortraitCatalog.ValidatePageImage(bytes, edit.Layout.Page);
                result.Add(new AtlasPixels { Edit = edit, Bytes = bytes });
            }
            return result.ToArray();
        }

        private static void ApplyAtlasPixels(AtlasSurface surface, AtlasPixels[] edits)
        {
            var composite = surface.Contents.ReadOriginalPixels();
            try
            {
                var pixels = composite.GetPixels32();
                foreach (var edit in edits)
                {
                    var image = new Texture2D(2, 2, TextureFormat.RGBA32, false, !surface.Texture.isDataSRGB);
                    try
                    {
                        if (!image.LoadImage(edit.Bytes)) throw new InvalidDataException("Atlas PNG decoding failed.");
                        edit.Edit.Layout.CopyPixels(image.GetPixels32(), pixels);
                    }
                    finally { UnityEngine.Object.Destroy(image); }
                }
                composite.SetPixels32(pixels); composite.Apply(false, false);
                surface.Contents.Apply(composite.EncodeToPNG());
            }
            finally { UnityEngine.Object.Destroy(composite); }
        }

        private static void RestoreAtlasSurface(AtlasSurface surface)
        {
            surface.Pending?.Dispose(); surface.Pending = null;
            if (surface.Contents != null)
            {
                try { if (surface.Texture != null) surface.Contents.Restore(); }
                finally { surface.Contents.Dispose(); surface.Contents = null; }
                if (surface.Applied != null)
                    foreach (var edit in surface.Applied) RecordAtlas(edit.Target.AtlasAddress, "restored", "original-atlas-pixels-restored", page: edit.Layout.Page.name);
            }
            surface.Applied = null;
        }

        private static void RecordAtlas(AtlasResourceAddress address, string stage, string outcome, string reason = null, string page = null)
        {
            ReplacementDiagnosticRuntime.Record(ReplacementDiagnosticTarget.Atlas(address), stage, "Atlas / ReplacementRuntime", outcome, reason,
                new Dictionary<string, object> { ["address"] = address.Describe(), ["pageKey"] = page });
        }

        private static void DescribeAtlasBinding(AtlasPageBinding page) => RecordAtlas(page.Atlas.Source.Address(page.Page.name, true),
            "entry-hit", "shared-page-bound", page: page.Page.name);

        private static void DiscoverAtlasBindings()
        {
            foreach (var binding in atlasBindings.Values)
                foreach (var page in binding.Pages.Values) DescribeAtlasBinding(page);
        }
    }
}
