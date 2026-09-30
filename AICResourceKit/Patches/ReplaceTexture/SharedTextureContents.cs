using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AICResourceKit.Patches.ReplaceTexture
{
    /// <summary>保留纹理对象引用，更新所有共享材质和图层；只保存停用所需的原像素。</summary>
    internal sealed class SharedTextureContents : IDisposable
    {
        private readonly Texture texture;
        private readonly bool keepUnreadable;
        private Texture2D original;

        internal SharedTextureContents(Texture texture)
        {
            this.texture = texture;
            keepUnreadable = texture is Texture2D image && !image.isReadable;
        }

        internal void Apply(byte[] bytes)
        {
            var candidate = new Texture2D(2, 2, TextureFormat.RGBA32, false, !texture.isDataSRGB);
            try
            {
                if (!candidate.LoadImage(bytes)) throw new InvalidDataException("PNG decoding failed.");
                if (candidate.width != texture.width || candidate.height != texture.height)
                    throw new InvalidDataException("Texture replacement dimensions must match the whole original image/page.");
                if (original == null) original = CaptureOriginal(texture);
                if (texture is Texture2D destination)
                {
                    // LoadImage 可以上传不可读纹理；Reinitialize/SetPixels 会被 Unity 拒绝。
                    if (!destination.LoadImage(bytes, keepUnreadable))
                        throw new InvalidDataException("Texture upload failed.");
                }
                else Blit(candidate, (RenderTexture)texture);
            }
            catch
            {
                // 首次写入失败后也恢复原像素，避免留下重建但尚未上传的纹理。
                if (original != null) Restore();
                throw;
            }
            finally { Object.Destroy(candidate); }
        }

        private static Texture2D CaptureOriginal(Texture source)
        {
            if (!(source is Texture2D image)) return ReadPixels(source);
            // 保留压缩块与全部 mip，恢复时不能把原图重新编码为 PNG/DXT。
            var copy = new Texture2D(image.width, image.height, image.format, image.mipmapCount > 1, !image.isDataSRGB);
            try
            {
                if (image.isReadable)
                {
                    copy.LoadRawTextureData(image.GetRawTextureData());
                    copy.Apply(false, false);
                }
                else
                {
                    copy.Apply(false, true);
                    Graphics.CopyTexture(image, copy);
                }
                return copy;
            }
            catch { Object.Destroy(copy); throw; }
        }

        private static Texture2D ReadPixels(Texture source)
        {
            if (!(source is Texture2D) && !(source is RenderTexture))
                throw new InvalidDataException("Texture replacement requires Texture2D or RenderTexture.");
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, !source.isDataSRGB);
            var previous = RenderTexture.active;
            var temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32,
                source.isDataSRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                copy.Apply(false, false);
                return copy;
            }
            catch { Object.Destroy(copy); throw; }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(temporary); }
        }

        private static void Blit(Texture pixels, RenderTexture destination)
        {
            var previous = RenderTexture.active;
            try { Graphics.Blit(pixels, destination); }
            finally { RenderTexture.active = previous; }
        }

        internal Texture2D ReadOriginalPixels()
        {
            if (original == null) original = CaptureOriginal(texture);
            return ReadPixels(original);
        }

        internal void Restore()
        {
            if (texture == null || original == null) return;
            if (texture is RenderTexture render) Blit(original, render);
            else if (texture.graphicsFormat == original.graphicsFormat && texture.mipmapCount == original.mipmapCount)
                Graphics.CopyTexture(original, texture);
            else
            {
                var pixels = ReadPixels(original);
                try
                {
                    if (!((Texture2D)texture).LoadImage(pixels.EncodeToPNG(), keepUnreadable))
                        throw new InvalidDataException("Original pixel restoration failed.");
                }
                finally { Object.Destroy(pixels); }
            }
        }

        public void Dispose()
        {
            if (original != null) Object.Destroy(original);
            original = null;
        }
    }
}
