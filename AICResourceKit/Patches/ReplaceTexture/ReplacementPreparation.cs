using Spine;
using AICResourceKit.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace AICResourceKit.Patches.ReplaceTexture
{
    internal sealed class PreparedSpine
    {
        internal byte[] ImageBytes;
        internal readonly Dictionary<string, byte[]> PageBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        internal string AtlasText;
        internal Atlas Atlas;
        internal SpineCompositionResult Composition;
    }

    /// <summary>只操作文件和独立的托管数据；不得访问 Unity 对象或实时配置。</summary>
    internal static class ReplacementPreparation
    {
        internal static PreparedSpine Spine(string originalJson, string originalAtlas, float originalScale,
            IReadOnlyList<ReplacementTarget> layers, string root, string sensitive, bool allowSensitive, CancellationToken token, bool multiplePages = false)
        {
            token.ThrowIfCancellationRequested();
            Validate(layers, root, sensitive, allowSensitive);
            token.ThrowIfCancellationRequested();
            string atlasPath = layers.Select(layer => layer.AtlasPath).LastOrDefault(path => path != null);
            string atlasText = atlasPath == null ? originalAtlas : ReplacementResourceIO.ReadText(atlasPath);
            var atlas = PortraitCatalog.ReadAtlas(atlasText);
            if (!multiplePages && atlas.Pages.Count != 1) throw new InvalidDataException("Only single-page Spine atlases are supported.");
            token.ThrowIfCancellationRequested();
            var prepared = new PreparedSpine { AtlasText = atlasText, Atlas = atlas };
            var imageLayer = layers.LastOrDefault(layer => layer.ImagePath != null || layer.PagePaths.Count > 0);
            if (multiplePages) SpinePageLayout.Validate(atlas);
            if (imageLayer?.PagePaths.Count > 0)
            {
                if (!multiplePages) throw new InvalidDataException("Portrait Spine replacement requires a single image.");
                ResourceAddressDraft.ValidatePages(atlas.Pages.Select(page => page.name),
                    imageLayer.Pages);
                foreach (var page in atlas.Pages)
                {
                    token.ThrowIfCancellationRequested();
                    byte[] bytes = ReplacementResourceIO.ReadBytes(imageLayer.PagePaths[page.name]);
                    PortraitCatalog.ValidateImage(bytes, atlas, page);
                    prepared.PageBytes.Add(page.name, bytes);
                }
            }
            else if (imageLayer != null)
            {
                prepared.ImageBytes = ReplacementResourceIO.ReadBytes(imageLayer.ImagePath);
                PortraitCatalog.ValidateImage(prepared.ImageBytes, atlas);
                prepared.PageBytes.Add(atlas.Pages[0].name, prepared.ImageBytes);
            }
            token.ThrowIfCancellationRequested();
            prepared.Composition = SpineComposer.Compose(originalJson, layers, atlas, originalScale, token);
            return prepared;
        }

        internal static byte[] Texture(ReplacementTarget layer, string root, string sensitive, bool allowSensitive, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Validate(new[] { layer }, root, sensitive, allowSensitive);
            var bytes = ReplacementResourceIO.ReadBytes(layer.ImagePath);
            token.ThrowIfCancellationRequested();
            return bytes;
        }

        internal static void Validate(IEnumerable<ReplacementTarget> layers, string root, string sensitive, bool allowSensitive)
        {
            foreach (var target in layers)
            {
                if (target.Owner == null) throw new InvalidDataException("Replacement package is missing.");
                if (target.Owner.Sensitive && !allowSensitive) throw new InvalidDataException("Sensitive content is disabled.");
                foreach (string path in new[] { target.Owner.ManifestPath, target.ImagePath, target.AtlasPath, target.JsonPath }.Concat(target.PagePaths.Values).Where(path => path != null))
                {
                    if (!File.Exists(path)) throw new FileNotFoundException("Replacement dependency was removed.", path);
                    PortraitCatalog.Resolve(root, Path.GetDirectoryName(path), Path.GetFileName(path));
                    if (PortraitCatalog.Within(sensitive, path) != target.Owner.Sensitive)
                        throw new InvalidDataException("Replacement dependencies crossed the Sensitive boundary.");
                }
            }
        }
    }
}
