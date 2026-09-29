using AICResourceKit.Patches.ReplaceTexture;

namespace AICResourceKit.Test.Patches.ReplaceTexture
{
    public sealed class LegacyResourceCompatibilityTests
    {
        // 由迁移前 BetterExperience 2.1.1 加密工具生成；保留固定密文以验证旧包兼容。
        [Fact]
        public void LegacyEncryptedPack_StillDecryptsAndDiscovers()
        {
            using var pack = new EncryptedPackFixture();
            string manifest = pack.Write("pack.replacement.json", Convert.FromBase64String("QkVSRUVOQwABAQAAAP26sN3dtcaEvnqNgAnHG42QAAAAAAAAADn2jwg447+pMSLnPUUIEdrEEM4ogLz7u4mIgLnD4irVIgh6yUDQrtVYEXUhQiiCmM/m+BbkQ8LvDQudDDICVwpJQLhEq0oaoy192U91EBXM3HhvH+iRjU7B6LOFc29BCl3qvVYbiUUqWC4qpKMd8gKgGmXhSwPHxts2jVj1UaEfQJVF6OQjsG+yzN5zsHRBrRJ1z0WXySEL9NIrpuBZ/0ZuQsdX7PhNdAKuD9rbk70W"));
            string image = pack.Write("page.png", Convert.FromBase64String("QkVSRUVOQwABAQAAAOzUwL2i6nh4y0T8v8aY4iBQAAAAAAAAAGv8JB/HiCB6KqHufYAgJmJ8Ejan5gaHQEAbLgZe48w2a/0fJAPiocid407SNvaCCpK8sTTl5hiNeUMs/R2F7HI8w9Fil+fTE11BqcwKjKcONkfOvnk9BSSxH0PZulWZRyD475C1rb2rrrfAJC4Lwrw="));

            Assert.Equal("""{"formatVersion":2,"id":"legacy-migration","targets":[{"type":"texture","loader":"mti","assetKey":"UI/Sheet","image":"page.png"}]}""", ReplacementResourceIO.ReadText(manifest));
            Assert.Equal(EncryptedPackFixture.Png, ReplacementResourceIO.ReadBytes(image));
            var catalog = pack.Discover();
            Assert.Empty(catalog.Errors);
            Assert.Equal("legacy-migration", Assert.Single(catalog.Packages).Id);
        }
    }
}
