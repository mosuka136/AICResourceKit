using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AICResourceKit.Contracts
{
    /// <summary>v2 清单公共语义。输入为 JSON 对象树，不依赖 JSON 解码器或游戏程序集。</summary>
    public sealed class ResourceManifest
    {
        public const int FormatVersion = 2;
        public string Id { get; private set; }
        public IReadOnlyList<ResourceTarget> Targets { get; private set; }

        /// <summary>作者工具使用的严格解析：任何无效目标或重复身份均拒绝整个导出。</summary>
        public static ResourceManifest Parse(Dictionary<string, object> json)
        {
            string id = ReadHeader(json, out var items);
            var targets = new List<ResourceTarget>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                var map = ContractValue.Object(item);
                string identity = IdentityOf(map);
                if (!identities.Add(identity)) throw new InvalidDataException("Duplicate target in package: " + identity.Replace('\n', '/'));
                targets.Add(ReadTarget(map));
            }
            return new ResourceManifest { Id = id, Targets = targets.AsReadOnly() };
        }

        /// <summary>运行时先读包头，再逐目标解析及隔离错误，不把坏目标当作成功。</summary>
        public static string ReadHeader(Dictionary<string, object> json, out List<object> targets)
        {
            if (ContractValue.Integer(ContractValue.Get(json, "formatVersion")) != FormatVersion)
                throw new InvalidDataException("Unsupported replacement manifest version.");
            string id = Required(json, "id");
            targets = ContractValue.Array(ContractValue.Get(json, "targets"));
            if (targets.Count == 0) throw new InvalidDataException("Replacement package has no targets.");
            return id;
        }

        public static string IdentityOf(Dictionary<string, object> json)
        {
            string type = Required(json, "type").ToLowerInvariant();
            if (type == "spine") return ResourceIdentity.Spine(Required(json, "key"), Required(json, "jsonKey"));
            if (type != "texture") throw new InvalidDataException("Target type must be texture or spine.");
            string loader = Required(json, "loader").ToLowerInvariant();
            if (loader == "mti")
                return ResourceIdentity.Mti(Required(json, "assetKey"), ContractValue.String(json, "imageKey"));
            if (loader == "resources")
                return ResourceIdentity.Resources(Required(json, "path"), Required(json, "objectType"));
            throw new InvalidDataException("Texture loader must be mti or resources.");
        }

        public static ResourceTarget ReadTarget(Dictionary<string, object> json) => ReadTarget<ResourceTarget>(json);

        internal static T ReadTarget<T>(Dictionary<string, object> json) where T : ResourceTarget, new()
        {
            var target = new T { Type = Required(json, "type").ToLowerInvariant() };
            if (target.Type == "texture")
            {
                target.Loader = Required(json, "loader").ToLowerInvariant();
                target.Image = Required(json, "image");
                if (target.Loader == "mti")
                {
                    target.AssetKey = Required(json, "assetKey");
                    target.ImageKey = ContractValue.String(json, "imageKey");
                }
                else if (target.Loader == "resources")
                {
                    target.ResourcePath = Required(json, "path");
                    target.ObjectType = Required(json, "objectType");
                    if (target.ObjectType != "Texture2D" && target.ObjectType != "Sprite")
                        throw new InvalidDataException("Resources objectType must be Texture2D or Sprite.");
                }
                else throw new InvalidDataException("Texture loader must be mti or resources.");
                return target;
            }
            if (target.Type != "spine") throw new InvalidDataException("Target type must be texture or spine.");
            target.SpineKey = Required(json, "key");
            target.JsonKey = Required(json, "jsonKey");
            string image = ContractValue.String(json, "image");
            string atlas = ContractValue.String(json, "atlas");
            target.Image = image;
            target.Atlas = atlas;
            object spineValue = ContractValue.Get(json, "spine");
            if (spineValue != null)
            {
                var spine = ContractValue.Object(spineValue);
                target.Json = Required(spine, "json");
                foreach (object sectionValue in ContractValue.Array(ContractValue.Get(spine, "replace")))
                {
                    string section = sectionValue as string ?? throw new InvalidDataException("Spine replace entries must be strings.");
                    if (!new[] { "bones", "slots", "constraints", "skins", "attachments", "events", "animations", "all" }.Contains(section))
                        throw new InvalidDataException("Unknown Spine replacement section: " + section);
                    target.Sections.Add(section);
                }
                if (target.Sections.Count == 0) throw new InvalidDataException("Spine replacement has no sections.");
                if (target.Sections.Contains("all") && target.Sections.Count != 1)
                    throw new InvalidDataException("all cannot be combined with other Spine replacement sections.");
                if (target.Sections.Contains("skins") && target.Sections.Contains("attachments"))
                    throw new InvalidDataException("skins and attachments cannot be selected in the same target layer.");
            }
            ReadCompatibility(json, target);
            ReadDisplay(json, target.Display);
            object effectsValue = ContractValue.Get(json, "effects");
            if (effectsValue != null)
            {
                target.Dirt = ContractValue.String(ContractValue.Object(effectsValue), "dirt", "auto");
                if (!new[] { "auto", "legacy", "disabled" }.Contains(target.Dirt))
                    throw new InvalidDataException("effects.dirt must be auto, legacy or disabled.");
            }
            if (target.Image == null && target.Atlas == null && target.Json == null
                && target.AnimationMap.Count == 0 && target.SkinMap.Count == 0 && target.BoneMap.Count == 0
                && !HasDisplay(target.Display) && target.Dirt == null)
                throw new InvalidDataException("Spine target does not replace anything.");
            return target;
        }

        private static void ReadCompatibility(Dictionary<string, object> json, ResourceTarget target)
        {
            object value = ContractValue.Get(json, "compatibility");
            if (value == null) return;
            var map = ContractValue.Object(value);
            ReadMap(map, "animations", target.AnimationMap);
            ReadMap(map, "skins", target.SkinMap);
            ReadMap(map, "bones", target.BoneMap);
            target.AnimationFallback = ContractValue.String(map, "animationFallback");
            target.SkinFallback = ContractValue.String(map, "skinFallback");
        }

        private static void ReadMap(Dictionary<string, object> parent, string key, Dictionary<string, string> output)
        {
            object value = ContractValue.Get(parent, key);
            if (value == null) return;
            foreach (var pair in ContractValue.Object(value))
            {
                string mapped = pair.Value as string;
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(mapped))
                    throw new InvalidDataException("Invalid compatibility mapping: " + key);
                output.Add(pair.Key, mapped);
            }
        }

        private static void ReadDisplay(Dictionary<string, object> json, ResourceDisplay display)
        {
            object value = ContractValue.Get(json, "display");
            if (value == null) return;
            var map = ContractValue.Object(value);
            display.SkeletonScale = OptionalNumber(map, "skeletonScale");
            display.ScaleMultiplier = OptionalNumber(map, "scaleMultiplier");
            display.OffsetX = OptionalNumber(map, "offsetX");
            display.OffsetY = OptionalNumber(map, "offsetY");
            display.Width = OptionalNumber(map, "width");
            display.Height = OptionalNumber(map, "height");
            display.RightShift = OptionalNumber(map, "rightShift");
            if ((display.SkeletonScale.HasValue && display.SkeletonScale.Value <= 0)
                || (display.ScaleMultiplier.HasValue && display.ScaleMultiplier.Value <= 0)
                || (display.Width.HasValue && display.Width.Value <= 0)
                || (display.Height.HasValue && display.Height.Value <= 0))
                throw new InvalidDataException("Display scale and dimensions must be positive.");
        }

        private static float? OptionalNumber(Dictionary<string, object> map, string key)
        {
            object value = ContractValue.Get(map, key);
            if (value == null) return null;
            float number = (float)ContractValue.Number(value);
            if (float.IsInfinity(number) || float.IsNaN(number)) throw new InvalidDataException("Display number exceeds Single range.");
            return number;
        }

        private static bool HasDisplay(ResourceDisplay value)
        {
            return value.SkeletonScale.HasValue || value.ScaleMultiplier.HasValue || value.OffsetX.HasValue
                || value.OffsetY.HasValue || value.Width.HasValue || value.Height.HasValue || value.RightShift.HasValue;
        }

        private static string Required(Dictionary<string, object> map, string key)
        {
            string value = ContractValue.String(map, key);
            if (string.IsNullOrWhiteSpace(value) || value.Contains("\n") || value.Contains("\r"))
                throw new InvalidDataException("Missing or invalid " + key);
            return value;
        }
    }
}
