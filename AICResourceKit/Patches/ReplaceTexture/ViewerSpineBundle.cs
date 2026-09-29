using HarmonyLib;
using Spine;
using Spine.Unity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AICResourceKit.Patches.ReplaceTexture
{
    /// <summary>一个普通查看器拥有自己的 atlas、骨架数据和页面材质，不改写 MTISpine 的共享缓存。</summary>
    internal sealed class ViewerSpineBundle : IDisposable
    {
        private sealed class PageMaterial
        {
            internal Material Material;
            internal Material Template;
            internal Texture Texture;
            internal bool Normal;
        }

        internal SpineAtlasAsset Atlas;
        internal SkeletonDataAsset Data;
        internal SpineCompositionResult Composition;
        internal Material[] Materials;
        internal readonly List<ReplacementPackage> Sources = new List<ReplacementPackage>();
        private readonly List<Texture> ownedImages = new List<Texture>();
        private readonly List<PageMaterial> pageMaterials = new List<PageMaterial>();
        private TextAsset atlasText;
        private TextAsset jsonText;
        private static readonly FieldInfo cachedAtlas = AccessTools.Field(typeof(SpineAtlasAsset), "atlas");
        private static readonly MethodInfo initialize = AccessTools.Method(typeof(SkeletonDataAsset), "InitializeWithData");

        internal static ViewerSpineBundle Build(PreparedSpine prepared, IReadOnlyList<ReplacementTarget> layers,
            SpineAtlasAsset originalAtlas, SkeletonDataAsset originalData, Texture originalTexture, Material template)
        {
            if (template == null) throw new InvalidDataException("The original Spine material is missing.");
            var bundle = new ViewerSpineBundle { Composition = prepared.Composition };
            try
            {
                bundle.Sources.AddRange(layers.Select(layer => layer.Owner).Distinct());
                var originals = OriginalTextures(originalAtlas, originalTexture);
                var normals = new List<Material>();
                var textures = new Dictionary<string, Texture>(StringComparer.Ordinal);
                foreach (var page in prepared.Atlas.Pages)
                {
                    originals.TryGetValue(page.name, out var source);
                    Texture image;
                    if (prepared.PageBytes.TryGetValue(page.name, out var bytes))
                    {
                        var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, source?.mipmapCount > 1,
                            source != null && !source.isDataSRGB);
                        bundle.ownedImages.Add(decoded);
                        if (!decoded.LoadImage(bytes)) throw new InvalidDataException("PNG decoding failed: " + page.name);
                        decoded.name = page.name;
                        decoded.filterMode = source?.filterMode ?? (page.magFilter == TextureFilter.Nearest ? FilterMode.Point : FilterMode.Bilinear);
                        decoded.wrapModeU = source?.wrapModeU ?? (page.uWrap == TextureWrap.Repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp);
                        decoded.wrapModeV = source?.wrapModeV ?? (page.vWrap == TextureWrap.Repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp);
                        decoded.anisoLevel = source?.anisoLevel ?? 1;
                        image = decoded;
                    }
                    else image = source;
                    if (image == null || image.width != page.width || image.height != page.height)
                        throw new InvalidDataException("Missing or mismatched original page; provide a complete pages mapping: " + page.name);
                    if (image.width > SystemInfo.maxTextureSize || image.height > SystemInfo.maxTextureSize)
                        throw new InvalidDataException("Spine page exceeds the device texture limit: " + page.name);
                    textures.Add(page.name, image);
                    var material = bundle.MakeMaterial(template, image, true);
                    normals.Add(material);
                    page.rendererObject = material;
                }
                bundle.Materials = normals.ToArray();
                bundle.atlasText = new TextAsset(prepared.AtlasText);
                bundle.jsonText = new TextAsset(prepared.Composition.Json);
                bundle.Atlas = ScriptableObject.CreateInstance<SpineAtlasAsset>();
                bundle.Atlas.atlasFile = bundle.atlasText;
                bundle.Atlas.materials = (Material[])bundle.Materials.Clone();
                cachedAtlas.SetValue(bundle.Atlas, prepared.Atlas);
                bundle.Data = ScriptableObject.CreateInstance<SkeletonDataAsset>();
                bundle.Data.atlasAssets = new AtlasAssetBase[] { bundle.Atlas };
                bundle.Data.skeletonJSON = bundle.jsonText;
                bundle.Data.scale = prepared.Composition.Display.SkeletonScale ?? originalData.scale;
                bundle.Data.defaultMix = originalData.defaultMix;
                bundle.Data.fromAnimation = (string[])originalData.fromAnimation.Clone();
                bundle.Data.toAnimation = (string[])originalData.toAnimation.Clone();
                bundle.Data.duration = (float[])originalData.duration.Clone();
                bundle.Data.blendModeMaterials = bundle.Blends(originalData.blendModeMaterials, textures);
                foreach (var modifier in originalData.skeletonDataModifiers)
                    if (modifier != null) modifier.Apply(prepared.Composition.PreparedData);
                bundle.Data.blendModeMaterials.ApplyMaterials(prepared.Composition.PreparedData);
                initialize.Invoke(bundle.Data, new object[] { prepared.Composition.PreparedData });
                return bundle;
            }
            catch { bundle.Dispose(); throw; }
        }

        private static Dictionary<string, Texture> OriginalTextures(SpineAtlasAsset atlas, Texture fallback)
        {
            var result = new Dictionary<string, Texture>(StringComparer.Ordinal);
            var metadata = PortraitCatalog.ReadAtlas(atlas.atlasFile.text);
            if (metadata.Pages.Count == 1 && fallback != null) result.Add(metadata.Pages[0].name, fallback);
            else
            {
                var loaded = atlas.GetAtlas(false);
                if (loaded != null)
                    foreach (var page in loaded.Pages)
                        if (page.rendererObject is Material material) result[page.name] = material.mainTexture;
            }
            return result;
        }

        private Material MakeMaterial(Material template, Texture texture, bool normal)
        {
            var material = new Material(template) { mainTexture = texture };
            pageMaterials.Add(new PageMaterial { Material = material, Template = template, Texture = texture, Normal = normal });
            return material;
        }

        private BlendModeMaterials Blends(BlendModeMaterials original, Dictionary<string, Texture> textures)
        {
            var result = new BlendModeMaterials
            { RequiresBlendModeMaterials = original.RequiresBlendModeMaterials, applyAdditiveMaterial = original.applyAdditiveMaterial };
            if (!result.RequiresBlendModeMaterials) return result;
            CopyBlend(original.additiveMaterials, result.additiveMaterials, textures);
            CopyBlend(original.multiplyMaterials, result.multiplyMaterials, textures);
            CopyBlend(original.screenMaterials, result.screenMaterials, textures);
            return result;
        }

        private void CopyBlend(List<BlendModeMaterials.ReplacementMaterial> source,
            List<BlendModeMaterials.ReplacementMaterial> destination, Dictionary<string, Texture> textures)
        {
            foreach (var page in textures)
            {
                var entry = source.FirstOrDefault(item => item.pageName == page.Key || item.pageName == null);
                if (entry?.material != null) destination.Add(new BlendModeMaterials.ReplacementMaterial
                { pageName = page.Key, material = MakeMaterial(entry.material, page.Value, false) });
            }
        }

        internal void SyncMaterials(Material normalTemplate)
        {
            foreach (var entry in pageMaterials)
            {
                var template = entry.Normal ? normalTemplate : entry.Template;
                if (template == null) continue;
                entry.Material.shader = template.shader;
                entry.Material.CopyPropertiesFromMaterial(template);
                entry.Material.mainTexture = entry.Texture;
            }
            // 游戏的 fineSpAtlasFirstMtr 可能写入首元素；恢复每页独立的材质。
            if (Atlas.materials == null || !Atlas.materials.SequenceEqual(Materials))
                Atlas.materials = (Material[])Materials.Clone();
        }

        public void Dispose()
        {
            if (Data != null) Object.Destroy(Data);
            if (Atlas != null) Object.Destroy(Atlas);
            if (atlasText != null) Object.Destroy(atlasText);
            if (jsonText != null) Object.Destroy(jsonText);
            foreach (var page in pageMaterials) if (page.Material != null) Object.Destroy(page.Material);
            foreach (var image in ownedImages) if (image != null) Object.Destroy(image);
        }
    }
}
