using System.Collections.Generic;
using System.IO;

namespace AICResourceKit.Contracts
{
    /// <summary>独立 atlas 的区域或整页地址，不包含骨架 JSON。</summary>
    public sealed class AtlasResourceAddress
    {
        public string Kind { get; private set; }
        public string Loader { get; private set; }
        public string AssetKey { get; private set; }
        public string AtlasKey { get; private set; }
        public string MemberKey { get; private set; }
        public string Identity { get; private set; }
        public bool IsPage => Kind == "atlas-page";

        public static AtlasResourceAddress Parse(Dictionary<string, object> json)
        {
            string kind = ContractValue.String(json, "kind");
            if (kind != "atlas-region" && kind != "atlas-page") throw new InvalidDataException("Expected atlas-region or atlas-page address.");
            string identity = ResourceAddressDraft.IdentityOf(json);
            return new AtlasResourceAddress
            {
                Kind = kind, Loader = ContractValue.String(json, "loader"), AssetKey = ContractValue.String(json, "assetKey"),
                AtlasKey = ContractValue.String(json, "atlasKey"), MemberKey = ContractValue.String(json, kind == "atlas-page" ? "pageKey" : "region"),
                Identity = kind + "\n" + identity.Substring("draft1|".Length)
            };
        }

        public static AtlasResourceAddress Create(string loader, string assetKey, string atlasKey, string memberKey, bool page = false)
        {
            var value = new Dictionary<string, object>
            {
                ["kind"] = page ? "atlas-page" : "atlas-region", ["loader"] = loader, ["atlasKey"] = atlasKey,
                [page ? "pageKey" : "region"] = memberKey
            };
            if (loader == "mti") value["assetKey"] = assetKey;
            return Parse(value);
        }

        public Dictionary<string, object> Describe()
        {
            var value = new Dictionary<string, object>
            { ["kind"] = Kind, ["loader"] = Loader, ["atlasKey"] = AtlasKey, [IsPage ? "pageKey" : "region"] = MemberKey };
            if (Loader == "mti") value["assetKey"] = AssetKey;
            return value;
        }

        public bool SameAtlas(AtlasResourceAddress other) => other != null && Loader == other.Loader && AssetKey == other.AssetKey && AtlasKey == other.AtlasKey;
        public bool MatchesSpine(SpineResourceAddress other) => other != null && Loader == other.Loader && AssetKey == other.AssetKey && AtlasKey == other.AtlasKey;
    }
}
