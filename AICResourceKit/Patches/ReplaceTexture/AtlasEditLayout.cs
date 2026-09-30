using AICResourceKit.Contracts;
using Spine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AICResourceKit.Patches.ReplaceTexture
{
    internal sealed class AtlasEditLayout
    {
        internal AtlasPage Page;
        internal int X, Y, Width, Height;

        internal static AtlasEditLayout Resolve(Atlas atlas, AtlasResourceAddress address)
        {
            if (atlas == null || atlas.Pages.Count == 0) throw new InvalidDataException("Atlas has no pages.");
            if (address.IsPage)
            {
                var page = atlas.Pages.SingleOrDefault(item => item.name == address.MemberKey)
                    ?? throw new InvalidDataException("Atlas page not found: " + address.MemberKey);
                ValidatePage(page);
                return new AtlasEditLayout { Page = page, Width = page.width, Height = page.height };
            }
            var region = atlas.Regions.SingleOrDefault(item => item.name == address.MemberKey)
                ?? throw new InvalidDataException("Atlas region not found: " + address.MemberKey);
            ValidatePage(region.page);
            if (region.x < 0 || region.y < 0 || region.width <= 0 || region.height <= 0
                || (long)region.x + region.width > region.page.width || (long)region.y + region.height > region.page.height)
                throw new InvalidDataException("Atlas region exceeds its page: " + region.name);
            // 当前游戏的 Atlas 读取器已交换旋转区域的宽高。坐标是原图左上角，不改 UV、trim 或旋转。
            var result = new AtlasEditLayout { Page = region.page, X = region.x, Y = region.y, Width = region.width, Height = region.height };
            if (atlas.Regions.Any(other => other != region && other.page == region.page
                && result.Overlaps(other.x, other.y, other.width, other.height)))
                throw new InvalidDataException("Region shares pixels with another atlas region; use an explicit atlas-page target: " + region.name);
            return result;
        }

        private static void ValidatePage(AtlasPage page)
        {
            if (page == null || page.width <= 0 || page.height <= 0 || page.pma)
                throw new InvalidDataException("Atlas pages require positive dimensions and straight alpha.");
        }

        private bool Overlaps(int x, int y, int width, int height) => X < (long)x + width && x < (long)X + Width
            && Y < (long)y + height && y < (long)Y + Height;

        internal bool Overlaps(AtlasEditLayout other) => Overlaps(other.X, other.Y, other.Width, other.Height);

        internal void CopyPixels<T>(T[] source, T[] destination)
        {
            long count = (long)Page.width * Page.height;
            if (source == null || destination == null || source.LongLength != count || destination.LongLength != count)
                throw new InvalidDataException("Candidate and original pixels must cover the complete atlas page.");
            int bottom = Page.height - Y - Height;
            for (int row = bottom; row < bottom + Height; row++)
                Array.Copy(source, row * Page.width + X, destination, row * Page.width + X, Width);
        }

        internal static void ValidateEdits(IReadOnlyList<AtlasEditLayout> edits)
        {
            for (int i = 0; i < edits.Count; i++)
                for (int j = i + 1; j < edits.Count; j++)
                    if (edits[i].Overlaps(edits[j]))
                        throw new InvalidDataException("Atlas targets overlap on the same texture; choose regions or one whole-page target.");
        }
    }
}
