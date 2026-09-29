namespace AICResourceKit.Contracts
{
    /// <summary>保持 v2 的大小写、空值和身份分隔符语义。</summary>
    public static class ResourceIdentity
    {
        public static string Spine(string key, string jsonKey) => "spine\n" + key + "\n" + jsonKey;
        public static string Mti(string assetKey, string imageKey) => "texture\nmti\n" + assetKey + "\n" + (imageKey ?? "");
        public static string Resources(string path, string objectType) => "texture\nresources\n" + path + "\n" + objectType;

        // 省略/null imageKey 是旧版容器通配；显式空串只匹配空串。身份串相同不等于匹配谓词相同。
        public static bool MatchesMti(string assetKey, string imageKey, string actualAssetKey, string actualImageKey) =>
            assetKey == actualAssetKey && (imageKey == null || imageKey == actualImageKey);
    }
}
