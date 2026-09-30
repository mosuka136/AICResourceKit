using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AICResourceKit.Contracts
{
    /// <summary>P02 地址草案。只校验地址及页映射，不注册运行时适配器，不是可安装的清单。</summary>
    public static class ResourceAddressDraft
    {
        public const int DraftVersion = 1;

        public static string IdentityOf(Dictionary<string, object> address)
        {
            string kind = Key(address, "kind");
            var parts = new List<string> { kind };
            var fields = new List<string> { "kind" };
            if (kind == "spine-assets" || kind == "atlas-region" || kind == "atlas-page")
            {
                string loader = Key(address, "loader");
                if (loader != "mti" && loader != "resources") throw new InvalidDataException("Draft loader must be mti or resources.");
                parts.Add(loader);
                fields.Add("loader");
                if (loader == "mti") Add(address, parts, fields, "assetKey");
                Add(address, parts, fields, "atlasKey");
                Add(address, parts, fields, kind == "spine-assets" ? "jsonKey" : kind == "atlas-page" ? "pageKey" : "region");
            }
            else if (kind == "pxl-image" || kind == "pxl-page")
            {
                var source = ContractValue.Object(ContractValue.Get(address, "source"));
                Only(source, "loader", "assetKey", "textKey");
                if (Key(source, "loader") != "mti") throw new InvalidDataException("Only observed MTI-backed PXL sources are specified in this draft.");
                parts.Add("mti");
                parts.Add(Key(source, "assetKey"));
                parts.Add(Key(source, "textKey"));
                fields.Add("source");
                if (kind == "pxl-image")
                {
                    string id = Key(address, "imageId");
                    if (!uint.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                        throw new InvalidDataException("imageId must be a UInt32 decimal string.");
                    parts.Add(number.ToString(CultureInfo.InvariantCulture));
                    string id2 = Key(address, "imageId2");
                    if (!double.TryParse(id2, NumberStyles.Float, CultureInfo.InvariantCulture, out var secondary)
                        || double.IsNaN(secondary) || double.IsInfinity(secondary))
                        throw new InvalidDataException("imageId2 must be a finite binary64 round-trip decimal string.");
                    // 用 binary64 位模式编码，避免不同 .NET 版本的 R 格式指数形式差异；正负零按值相等。
                    parts.Add(BitConverter.DoubleToInt64Bits(secondary == 0 ? 0 : secondary).ToString("X16", CultureInfo.InvariantCulture));
                    string role = Key(address, "role");
                    if (role != "I" && role != "P") throw new InvalidDataException("PXL role must be I or P.");
                    parts.Add(role);
                    fields.AddRange(new[] { "imageId", "imageId2", "role" });
                }
                else
                {
                    string storage = Key(address, "storage");
                    parts.Add(storage);
                    fields.Add("storage");
                    if (storage == "external") AddIndex(address, parts, fields, "pageIndex");
                    else if (storage == "packed")
                    {
                        AddIndex(address, parts, fields, "pageOrdinal");
                        AddIndex(address, parts, fields, "imageType");
                    }
                    else throw new InvalidDataException("PXL storage must be external or packed.");
                }
            }
            else if (kind == "video")
            {
                Add(address, parts, fields, "assetKey");
                Add(address, parts, fields, "clipKey");
            }
            else throw new InvalidDataException("Unspecified draft address kind: " + kind);
            Only(address, fields.ToArray());
            return "draft1|" + string.Concat(parts.Select(part => Encoding.UTF8.GetByteCount(part).ToString(CultureInfo.InvariantCulture) + ":" + part));
        }

        /// <summary>完整页映射须与运行时 atlas/页目录的原始键集合一一对应，禁止推断或补齐缺页。</summary>
        public static IReadOnlyList<ResourceDependency> ValidatePages(IEnumerable<string> expectedPageKeys,
            IEnumerable<ResourcePageDraft> mappings)
        {
            if (expectedPageKeys == null || mappings == null) throw new InvalidDataException("Page keys and mappings are required.");
            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in expectedPageKeys)
                if (string.IsNullOrWhiteSpace(key) || !expected.Add(key)) throw new InvalidDataException("Invalid or duplicate original page key.");
            if (expected.Count == 0) throw new InvalidDataException("The original page inventory is empty.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<ResourceDependency>();
            foreach (var page in mappings)
            {
                if (page == null || page.PageKey == null || !expected.Contains(page.PageKey) || !seen.Add(page.PageKey))
                    throw new InvalidDataException("Unknown or duplicate mapped page key.");
                if (string.IsNullOrWhiteSpace(page.Image) || page.Image.StartsWith("/", StringComparison.Ordinal)
                    || page.Image.StartsWith("\\", StringComparison.Ordinal) || page.Image.IndexOf(':') >= 0
                    || page.Image.IndexOf('\0') >= 0 || page.Image.IndexOf('\r') >= 0 || page.Image.IndexOf('\n') >= 0)
                    throw new InvalidDataException("Expected relative page image path.");
                result.Add(new ResourceDependency("image", page.Image));
            }
            if (!expected.SetEquals(seen)) throw new InvalidDataException("Missing page mapping.");
            return result.AsReadOnly();
        }

        private static string Key(Dictionary<string, object> value, string key)
        {
            string text = ContractValue.String(value, key);
            if (string.IsNullOrWhiteSpace(text) || text.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new InvalidDataException("Missing or invalid draft key: " + key);
            // 来源与运行时键不允许使用磁盘绝对路径、父目录导航或导出文件的定位方式。
            if (text.StartsWith("/", StringComparison.Ordinal) || text.StartsWith("\\", StringComparison.Ordinal)
                || text.IndexOf(':') >= 0 || text.Split('/', '\\').Any(segment => segment == ".."))
                throw new InvalidDataException("Draft address keys must be logical loading keys.");
            return text;
        }

        private static void Add(Dictionary<string, object> value, List<string> parts, List<string> fields, string name)
        {
            parts.Add(Key(value, name));
            fields.Add(name);
        }

        private static void AddIndex(Dictionary<string, object> value, List<string> parts, List<string> fields, string name)
        {
            int index = ContractValue.Integer(ContractValue.Get(value, name));
            if (index < 0) throw new InvalidDataException("Page indices must be nonnegative.");
            parts.Add(index.ToString(CultureInfo.InvariantCulture));
            fields.Add(name);
        }

        private static void Only(Dictionary<string, object> value, params string[] fields)
        {
            if (value == null || value.Keys.Any(key => !fields.Contains(key, StringComparer.Ordinal)))
                throw new InvalidDataException("Unknown draft address field.");
        }
    }

    public sealed class ResourcePageDraft
    {
        public string PageKey { get; }
        public string Image { get; }
        public ResourcePageDraft(string pageKey, string image) { PageKey = pageKey; Image = image; }
    }
}
