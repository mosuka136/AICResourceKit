using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AICResourceKit.Patches.ReplaceTexture
{
    // 未打包 Sprite 沿用逻辑 rect 与原网格；裁片纹理不能冒充整张源纹理。
    internal sealed class ReplacementSpriteLayout
    {
        internal readonly Rect Rect;
        internal readonly Vector2 Pivot;
        internal readonly float PixelsPerUnit;
        internal readonly Vector4 Border;
        internal readonly Vector2[] Vertices;
        internal readonly ushort[] Triangles;

        internal static ReplacementSpriteLayout Read(Sprite sprite)
        {
            return new ReplacementSpriteLayout(sprite.rect, sprite.pivot, sprite.pixelsPerUnit, sprite.border,
                sprite.texture.width, sprite.texture.height, sprite.packed, sprite.vertices, sprite.triangles, sprite.uv);
        }

        internal ReplacementSpriteLayout(Rect rect, Vector2 pivotPixels, float pixelsPerUnit, Vector4 border,
            int textureWidth, int textureHeight, bool packed, Vector2[] vertices, ushort[] triangles, Vector2[] uv)
        {
            if (packed) throw new InvalidDataException("Packed or rotated Sprite replacement is not supported.");
            if (textureWidth <= 0 || textureHeight <= 0 || !Finite(rect.x) || !Finite(rect.y)
                || !Positive(rect.width) || !Positive(rect.height) || rect.x < 0 || rect.y < 0
                || rect.xMax > textureWidth || rect.yMax > textureHeight)
                throw new InvalidDataException("Sprite rect must fit inside the full source texture.");
            if (!Positive(pixelsPerUnit) || !Finite(pivotPixels.x) || !Finite(pivotPixels.y))
                throw new InvalidDataException("Sprite pivot and pixelsPerUnit are invalid.");
            if (vertices == null || uv == null || vertices.Length < 3 || vertices.Length != uv.Length
                || triangles == null || triangles.Length == 0 || triangles.Length % 3 != 0)
                throw new InvalidDataException("Sprite mesh is missing or incomplete.");
            foreach (ushort index in triangles)
                if (index >= vertices.Length) throw new InvalidDataException("Sprite triangle index is out of range.");
            for (int i = 0; i < vertices.Length; i++)
            {
                // OverrideGeometry 按原 rect/pivot/PPU 推导 UV；不接受需要额外旋转或图集偏移的映射。
                float u = (vertices[i].x * pixelsPerUnit + pivotPixels.x + rect.x) / textureWidth;
                float v = (vertices[i].y * pixelsPerUnit + pivotPixels.y + rect.y) / textureHeight;
                if (!Finite(u) || !Finite(v) || !Finite(uv[i].x) || !Finite(uv[i].y)
                    || Math.Abs(u - uv[i].x) > 0.0001f || Math.Abs(v - uv[i].y) > 0.0001f)
                    throw new InvalidDataException("Sprite UV requires an unsupported packed or transformed layout.");
            }
            Rect = rect;
            Pivot = new Vector2(pivotPixels.x / rect.width, pivotPixels.y / rect.height);
            PixelsPerUnit = pixelsPerUnit;
            Border = border;
            Vertices = vertices;
            Triangles = triangles;
        }

        internal Sprite Create(Texture2D texture)
        {
            var sprite = Sprite.Create(texture, Rect, Pivot, PixelsPerUnit, 0, SpriteMeshType.FullRect, Border);
            if (sprite == null) throw new InvalidDataException("Sprite creation failed.");
            try
            {
                sprite.OverrideGeometry(Vertices, Triangles);
                return sprite;
            }
            catch
            {
                Object.Destroy(sprite);
                throw;
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Positive(float value) => Finite(value) && value > 0;
    }
}
