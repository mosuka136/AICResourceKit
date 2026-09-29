using System.Collections.Generic;
using System.IO;

namespace AICResourceKit.Contracts
{
    /// <summary>普通 SpineViewer 的实际文本来源，与主立绘 key/jsonKey 分开。</summary>
    public sealed class SpineResourceAddress
    {
        public string Loader { get; private set; }
        public string AssetKey { get; private set; }
        public string AtlasKey { get; private set; }
        public string JsonKey { get; private set; }
        public string Identity { get; private set; }

        public static SpineResourceAddress Parse(Dictionary<string, object> json)
        {
            if (ContractValue.String(json, "kind") != "spine-assets")
                throw new InvalidDataException("Expected a spine-assets address.");
            string identity = ResourceAddressDraft.IdentityOf(json);
            return new SpineResourceAddress
            {
                Loader = ContractValue.String(json, "loader"), AssetKey = ContractValue.String(json, "assetKey"),
                AtlasKey = ContractValue.String(json, "atlasKey"), JsonKey = ContractValue.String(json, "jsonKey"),
                Identity = "spine-assets\n" + identity.Substring("draft1|".Length)
            };
        }

        public Dictionary<string, object> Describe()
        {
            var value = new Dictionary<string, object>
            { ["kind"] = "spine-assets", ["loader"] = Loader, ["atlasKey"] = AtlasKey, ["jsonKey"] = JsonKey };
            if (Loader == "mti") value["assetKey"] = AssetKey;
            return value;
        }

        public static SpineResourceAddress Create(string loader, string assetKey, string atlasKey, string jsonKey)
        {
            var value = new Dictionary<string, object>
            { ["kind"] = "spine-assets", ["loader"] = loader, ["atlasKey"] = atlasKey, ["jsonKey"] = jsonKey };
            if (loader == "mti") value["assetKey"] = assetKey;
            return Parse(value);
        }
    }
}
