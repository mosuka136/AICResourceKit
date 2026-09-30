using AICResourceKit.Contracts;
using nel;
using PixelLiner;
using System.Collections.Generic;
using System.Linq;

namespace AICResourceKit.Patches.ReplaceTexture
{
    internal static partial class ReplacementRuntime
    {
        internal static List<object> DescribeMpccDependencies(string character, string[] parts)
        {
            var result = new List<object>();
            string[] noelTitles = MTR.Anoel_pxls.SelectMany(group => group).ToArray();
            foreach (var pair in pxlCharacters)
                if (MpccFileHeader.MatchesSource(character, pair.Value.Source.AssetKey, pair.Key.title, noelTitles))
                    result.Add(DescribeMpccCharacter(pair.Key, parts));
            return result;
        }

        internal static Dictionary<string, object> DescribeMpccCharacter(PxlCharacter character, string[] parts = null)
        {
            var result = new Dictionary<string, object> { ["characterTitle"] = character?.title, ["sourceVerified"] = false };
            if (character == null || !pxlCharacters.TryGetValue(character, out var record)) return result;
            result["sourceVerified"] = true;
            result["source"] = new Dictionary<string, object>
                { ["loader"] = "mti", ["assetKey"] = record.Source.AssetKey, ["textKey"] = record.Source.TextKey };
            var availableParts = character.APartsInfo?.Select(part => part.name).ToArray() ?? new string[0];
            result["partsMetadataAvailable"] = availableParts.Length > 0;
            if (parts != null)
            {
                result["matchedParts"] = parts.Where(part => availableParts.Contains(part)).ToArray();
                result["missingParts"] = parts.Where(part => !availableParts.Contains(part)).ToArray();
            }
            // AddChr 的输入固定为外部槽位 0（原色）与 1（部件遮罩）；只输出实际存在的槽位。
            var textures = character.getExternalTextureArray();
            result["partsMaskLoaded"] = textures != null && textures.Length > 1 && textures[1]?.Image != null;
            var pages = new List<object>();
            if (textures != null)
                for (int index = 0; index < textures.Length && index < 2; index++)
                {
                    if (textures[index] == null) continue;
                    var texture = textures[index].Image;
                    var page = new Dictionary<string, object>
                    {
                        ["role"] = index == 0 ? "source-color" : "parts-mask", ["loaded"] = texture != null,
                        ["address"] = PxlResourceAddress.Page(record.Source.AssetKey, record.Source.TextKey, index).Describe()
                    };
                    if (texture != null) { page["width"] = texture.width; page["height"] = texture.height; }
                    pages.Add(page);
                }
            result["pages"] = pages;
            return result;
        }
    }
}
