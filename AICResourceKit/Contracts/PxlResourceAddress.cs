using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AICResourceKit.Contracts
{
    /// <summary>PXL 的实际 MTI 来源与图片/整页地址，不使用角色显示名或导出文件名。</summary>
    public sealed class PxlResourceAddress
    {
        public string AssetKey { get; private set; }
        public string TextKey { get; private set; }
        public string Kind { get; private set; }
        public string Storage { get; private set; }
        public uint ImageId { get; private set; }
        public double ImageId2 { get; private set; }
        public string Role { get; private set; }
        public int PageIndex { get; private set; }
        public int PageOrdinal { get; private set; }
        public int ImageType { get; private set; }
        public string Identity { get; private set; }

        public static PxlResourceAddress Parse(Dictionary<string, object> json)
        {
            string kind = ContractValue.String(json, "kind");
            if (kind != "pxl-image" && kind != "pxl-page")
                throw new InvalidDataException("PXL address kind must be pxl-image or pxl-page.");
            // 沿用 P02 已验证的 ID 精度、来源边界与字段约束，运行时身份使用独立前缀。
            string identity = ResourceAddressDraft.IdentityOf(json);
            var source = ContractValue.Object(ContractValue.Get(json, "source"));
            var result = new PxlResourceAddress
            {
                Kind = kind, AssetKey = ContractValue.String(source, "assetKey"),
                TextKey = ContractValue.String(source, "textKey"),
                Identity = "texture\npxl\n" + identity.Substring("draft1|".Length)
            };
            if (kind == "pxl-image")
            {
                result.ImageId = uint.Parse(ContractValue.String(json, "imageId"), CultureInfo.InvariantCulture);
                result.ImageId2 = double.Parse(ContractValue.String(json, "imageId2"), CultureInfo.InvariantCulture);
                result.Role = ContractValue.String(json, "role");
            }
            else
            {
                result.Storage = ContractValue.String(json, "storage");
                if (result.Storage == "external") result.PageIndex = ContractValue.Integer(json["pageIndex"]);
                else
                {
                    result.PageOrdinal = ContractValue.Integer(json["pageOrdinal"]);
                    result.ImageType = ContractValue.Integer(json["imageType"]);
                    if (result.ImageType > 2) throw new InvalidDataException("PXL imageType must be 0, 1 or 2.");
                }
            }
            return result;
        }

        public Dictionary<string, object> Describe()
        {
            var json = new Dictionary<string, object>
            {
                ["kind"] = Kind, ["source"] = new Dictionary<string, object>
                { ["loader"] = "mti", ["assetKey"] = AssetKey, ["textKey"] = TextKey }
            };
            if (Kind == "pxl-image")
            {
                json["imageId"] = ImageId.ToString(CultureInfo.InvariantCulture);
                json["imageId2"] = ImageId2.ToString("R", CultureInfo.InvariantCulture);
                json["role"] = Role;
            }
            else
            {
                json["storage"] = Storage;
                if (Storage == "external") json["pageIndex"] = PageIndex;
                else { json["pageOrdinal"] = PageOrdinal; json["imageType"] = ImageType; }
            }
            return json;
        }

        public static PxlResourceAddress Embedded(string assetKey, string textKey, uint id, double id2, string role) =>
            Parse(new Dictionary<string, object>
            {
                ["kind"] = "pxl-image", ["source"] = Source(assetKey, textKey),
                ["imageId"] = id.ToString(CultureInfo.InvariantCulture),
                ["imageId2"] = id2.ToString("R", CultureInfo.InvariantCulture), ["role"] = role
            });

        public static PxlResourceAddress Page(string assetKey, string textKey, int index, int? imageType = null)
        {
            var json = new Dictionary<string, object>
            {
                ["kind"] = "pxl-page", ["source"] = Source(assetKey, textKey),
                ["storage"] = imageType.HasValue ? "packed" : "external"
            };
            if (imageType.HasValue) { json["pageOrdinal"] = index; json["imageType"] = imageType.Value; }
            else json["pageIndex"] = index;
            return Parse(json);
        }

        private static Dictionary<string, object> Source(string assetKey, string textKey) =>
            new Dictionary<string, object> { ["loader"] = "mti", ["assetKey"] = assetKey, ["textKey"] = textKey };
    }
}
