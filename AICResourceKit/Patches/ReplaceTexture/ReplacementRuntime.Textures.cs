using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AICResourceKit.Patches.ReplaceTexture
{
    // MTI 与 Resources 共用的纹理准备、上传节流和原位更新。
    internal static partial class ReplacementRuntime
    {
        private static int uploadFrame = -1;

        private static bool ClaimUpload()
        {
            if (uploadFrame == Time.frameCount) return false;
            uploadFrame = Time.frameCount;
            return true;
        }

        private static ReplacementWork<byte[]> PrepareTexture(ReplacementTarget layer)
        {
            string root = PatchInfo.ReplaceImagePath, sensitive = PatchInfo.ReplaceSensitiveImagePath;
            bool allow = selection.AllowSensitive;
            return new ReplacementWork<byte[]>(token => ReplacementPreparation.Texture(layer, root, sensitive, allow, token));
        }

        private static Texture2D LoadStableTexture(byte[] bytes, Texture source, Texture2D stable)
        {
            var candidate = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!candidate.LoadImage(bytes))
            {
                Object.Destroy(candidate);
                throw new InvalidDataException("PNG decoding failed.");
            }
            if (candidate.width != source.width || candidate.height != source.height)
            {
                Object.Destroy(candidate);
                throw new InvalidDataException("Replacement image dimensions must match the original resource.");
            }
            if (stable == null) stable = candidate;
            else
            {
                if (!stable.LoadImage(bytes))
                {
                    Object.Destroy(candidate);
                    throw new InvalidDataException("Stable texture refresh failed.");
                }
                Object.Destroy(candidate);
            }
            CopyTextureProperties(source, stable);
            return stable;
        }

        // 从游戏仍持有的原纹理恢复像素，保持已返回给调用方的 Texture/Sprite 引用。
        private static void RestoreTextureContents(Texture source, Texture2D destination)
        {
            var previous = RenderTexture.active;
            var temporary = RenderTexture.GetTemporary(source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                destination.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                destination.Apply(false, false);
                CopyTextureProperties(source, destination);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static void CopyTextureProperties(Texture source, Texture destination)
        {
            destination.name = source.name;
            destination.filterMode = source.filterMode;
            destination.wrapMode = source.wrapMode;
            destination.wrapModeU = source.wrapModeU;
            destination.wrapModeV = source.wrapModeV;
            destination.wrapModeW = source.wrapModeW;
            destination.anisoLevel = source.anisoLevel;
            destination.mipMapBias = source.mipMapBias;
            destination.hideFlags = source.hideFlags;
        }
    }
}
