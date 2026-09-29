using Spine;
using System;
using System.Collections.Generic;
using System.IO;

namespace AICResourceKit.Patches.ReplaceTexture
{
    internal static class SpinePageLayout
    {
        internal static void Validate(Atlas atlas)
        {
            if (atlas == null || atlas.Pages.Count == 0) throw new InvalidDataException("Spine atlas has no pages.");
            var pages = new HashSet<string>(StringComparer.Ordinal);
            foreach (var page in atlas.Pages)
            {
                if (string.IsNullOrWhiteSpace(page.name) || !pages.Add(page.name))
                    throw new InvalidDataException("Missing or duplicate atlas page name.");
                if (page.width <= 0 || page.height <= 0 || page.pma)
                    throw new InvalidDataException("Spine pages require positive dimensions and straight alpha.");
            }
            var regions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var region in atlas.Regions)
            {
                if (!regions.Add(region.name)) throw new InvalidDataException("Duplicate atlas region: " + region.name);
                if (!atlas.Pages.Contains(region.page) || region.x < 0 || region.y < 0 || region.width <= 0 || region.height <= 0
                    || (long)region.x + region.width > region.page.width || (long)region.y + region.height > region.page.height
                    || region.originalWidth <= 0 || region.originalHeight <= 0 || (region.degrees != 0 && region.degrees != 90))
                    throw new InvalidDataException("Invalid atlas region: " + region.name);
            }
        }
    }
}
