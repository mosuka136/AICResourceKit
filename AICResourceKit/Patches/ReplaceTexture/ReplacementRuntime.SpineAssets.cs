using AICResourceKit.BLogSpace;
using HarmonyLib;
using nel;
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
    // 将后台准备结果构建为 Unity Spine 资源，并管理这些对象的所有权。
    internal static partial class ReplacementRuntime
    {
        private sealed class SpineBundle : IDisposable
        {
            internal readonly List<ReplacementPackage> Sources = new List<ReplacementPackage>();
            internal Texture Image;
            internal bool OwnImage;
            internal TextAsset JsonText;
            internal TextAsset AtlasText;
            internal SpineAtlasAsset Atlas;
            internal SkeletonDataAsset Data;
            internal Material StagingMaterial;
            internal SpineCompositionResult Composition;

            internal void Bind(Material[] materials)
            {
                if (materials == null || materials.Length == 0) throw new InvalidOperationException("Spine materials are missing.");
                Atlas.materials = materials;
                foreach (var page in Atlas.GetAtlas(false).Pages) page.rendererObject = materials[0];
            }

            public void Dispose()
            {
                if (Data != null) Object.Destroy(Data);
                if (Atlas != null) Object.Destroy(Atlas);
                if (JsonText != null) Object.Destroy(JsonText);
                if (AtlasText != null) Object.Destroy(AtlasText);
                if (StagingMaterial != null) Object.Destroy(StagingMaterial);
                if (OwnImage && Image != null) Object.Destroy(Image);
            }
        }

        private sealed class MaterialLoader : TextureLoader
        {
            private readonly Material material;
            internal MaterialLoader(Material material) { this.material = material; }
            public void Load(AtlasPage page, string path) { page.rendererObject = material; }
            public void Unload(object texture) { }
        }

        private static readonly FieldInfo cachedAtlas = AccessTools.Field(typeof(SpineAtlasAsset), "atlas");
        private static readonly MethodInfo initializeData = AccessTools.Method(typeof(SkeletonDataAsset), "InitializeWithData", new[] { typeof(SkeletonData) });

        private static SpineBundle BuildSpineBundle(BetobetoManager.SvTexture texture, List<ReplacementTarget> layers,
            Material material, PreparedSpine prepared, SkeletonDataAsset originalData)
        {
            var bundle = new SpineBundle();
            try
            {
                foreach (var layer in layers) ValidateCurrent(layer);
                foreach (var source in layers.Select(layer => layer.Owner).Distinct()) bundle.Sources.Add(source);
                string atlasText = prepared.AtlasText;
                var metadataAtlas = prepared.Atlas;
                string imagePath = LastPath(layers, layer => layer.ImagePath);
                if (imagePath != null)
                {
                    var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!image.LoadImage(prepared.ImageBytes))
                    {
                        Object.Destroy(image);
                        throw new InvalidDataException("PNG decoding failed.");
                    }
                    image.name = Path.GetFileNameWithoutExtension(imagePath);
                    image.wrapMode = TextureWrapMode.Clamp;
                    image.filterMode = FilterMode.Bilinear;
                    bundle.Image = image;
                    bundle.OwnImage = true;
                }
                else
                {
                    bundle.Image = texture.MtiImage0.Image;
                    if (bundle.Image == null) throw new InvalidDataException("Original Spine texture is not loaded yet.");
                    ValidateAtlasTexture(metadataAtlas, bundle.Image);
                }
                if (bundle.Image.width > SystemInfo.maxTextureSize || bundle.Image.height > SystemInfo.maxTextureSize)
                    throw new InvalidDataException("Spine atlas exceeds the device texture size limit.");
                bundle.Composition = prepared.Composition;
                if (bundle.Composition.DirtMode == "auto" && !bundle.Composition.DirtEnabled)
                    BLog.Info("Spine dirt effect disabled because the composed atlas has no compatible EM/ND region: "
                        + texture.key + "/" + layers[0].JsonKey);
                bundle.StagingMaterial = new Material(material) { mainTexture = bundle.Image };
                bundle.AtlasText = new TextAsset(atlasText);
                bundle.JsonText = new TextAsset(bundle.Composition.Json);
                bundle.Atlas = SpineAtlasAsset.CreateRuntimeInstance(bundle.AtlasText,
                    new[] { bundle.StagingMaterial }, false, asset => new MaterialLoader(bundle.StagingMaterial));
                // 后台 Atlas 已按 Unity 的约定翻转 UV；沿用同一对象，避免附件引用旧图集或二次翻转。
                foreach (var page in metadataAtlas.Pages) page.rendererObject = bundle.StagingMaterial;
                cachedAtlas.SetValue(bundle.Atlas, metadataAtlas);
                bundle.Data = ScriptableObject.CreateInstance<SkeletonDataAsset>();
                bundle.Data.atlasAssets = new AtlasAssetBase[] { bundle.Atlas };
                bundle.Data.skeletonJSON = bundle.JsonText;
                bundle.Data.scale = bundle.Composition.Display.SkeletonScale ?? originalData.scale;
                bundle.Data.defaultMix = originalData.defaultMix;
                bundle.Data.fromAnimation = originalData.fromAnimation == null
                    ? new string[0] : (string[])originalData.fromAnimation.Clone();
                bundle.Data.toAnimation = originalData.toAnimation == null
                    ? new string[0] : (string[])originalData.toAnimation.Clone();
                bundle.Data.duration = originalData.duration == null
                    ? new float[0] : (float[])originalData.duration.Clone();
                initializeData.Invoke(bundle.Data, new object[] { bundle.Composition.PreparedData });
                bundle.Bind(new[] { material });
                return bundle;
            }
            catch
            {
                bundle.Dispose();
                throw;
            }
        }

        private static string LastPath(IEnumerable<ReplacementTarget> layers, Func<ReplacementTarget, string> selector)
        {
            string result = null;
            foreach (var layer in layers)
            {
                string value = selector(layer);
                if (value != null) result = value;
            }
            return result;
        }

        private static void ValidateAtlasTexture(Atlas atlas, Texture image)
        {
            var page = atlas.Pages[0];
            if (page.width != image.width || page.height != image.height)
                throw new InvalidDataException("Texture dimensions do not match the single-page atlas.");
            if (page.pma) throw new InvalidDataException("Export a straight-alpha atlas (PMA is unsupported).");
            foreach (var region in atlas.Regions)
                if (region.x < 0 || region.y < 0 || region.width <= 0 || region.height <= 0
                    || (long)region.x + region.width > image.width || (long)region.y + region.height > image.height)
                    throw new InvalidDataException("Invalid atlas bounds: " + region.name);
        }
    }
}
