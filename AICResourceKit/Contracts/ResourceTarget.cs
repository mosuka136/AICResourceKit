using System;
using System.Collections.Generic;

namespace AICResourceKit.Contracts
{
    /// <summary>与 Unity 对象无关的清单数据；每次解析返回独立实例。</summary>
    public sealed class ResourceDisplay
    {
        public float? SkeletonScale;
        public float? ScaleMultiplier;
        public float? OffsetX;
        public float? OffsetY;
        public float? Width;
        public float? Height;
        public float? RightShift;
    }

    public class ResourceTarget
    {
        public string Type;
        public string Loader;
        public string AssetKey;
        public string ImageKey;
        public string ResourcePath;
        public string ObjectType;
        public string SpineKey;
        public string JsonKey;
        public string Image;
        public string Atlas;
        public string Json;
        public readonly HashSet<string> Sections = new HashSet<string>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> AnimationMap = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> SkinMap = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> BoneMap = new Dictionary<string, string>(StringComparer.Ordinal);
        public string AnimationFallback;
        public string SkinFallback;
        public ResourceDisplay Display = new ResourceDisplay();
        public string Dirt;

        /// <summary>清单显式引用的依赖；路径相对于清单，尚未解析到磁盘。</summary>
        public IEnumerable<ResourceDependency> Dependencies
        {
            get
            {
                if (Image != null) yield return new ResourceDependency("image", Image);
                if (Atlas != null) yield return new ResourceDependency("atlas", Atlas);
                if (Json != null) yield return new ResourceDependency("json", Json);
            }
        }

        public string Identity
        {
            get
            {
                if (Type == "spine") return ResourceIdentity.Spine(SpineKey, JsonKey);
                if (Loader == "mti") return ResourceIdentity.Mti(AssetKey, ImageKey);
                return ResourceIdentity.Resources(ResourcePath, ObjectType);
            }
        }
    }

    public sealed class ResourceDependency
    {
        public string Kind { get; }
        public string Path { get; }
        public ResourceDependency(string kind, string path) { Kind = kind; Path = path; }
    }
}
