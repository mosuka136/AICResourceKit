extern alias ResourceEncryptor;

using AICResourceKit.Contracts;
using AICResourceKit.Patches.ReplaceTexture;
using ToolEncryptor = ResourceEncryptor::AICResourceKit.ResourceEncryptor.PackEncryptor;

namespace AICResourceKit.Test.Patches.ReplaceTexture
{
    public sealed class AtlasReplacementTests
    {
        private const string AtlasText = "a.png\nsize: 4,3\nfilter: Nearest,Nearest\nleft\nbounds: 0,0,2,1\nright\nbounds: 2,0,2,1\nrotated\nrotate: 90\nbounds: 0,1,1,2\noffsets: 3,4,10,10\n\nb.png\nsize: 1,1\nother\nbounds: 0,0,1,1\n";
        private static AtlasResourceAddress Address(string key, bool page = false) =>
            AtlasResourceAddress.Create("mti", "Fatal/shared.atlas", "shared.atlas", key, page);

        [Fact]
        public void RegionCopy_UsesTopLeftAtlasBoundsAndKeepsOtherPixels()
        {
            var atlas = PortraitCatalog.ReadAtlas(AtlasText);
            var layout = AtlasEditLayout.Resolve(atlas, Address("left"));
            var original = Enumerable.Repeat(1, 12).ToArray();
            layout.CopyPixels(Enumerable.Repeat(9, 12).ToArray(), original);
            Assert.Equal(new[] { 1, 1, 1, 1, 1, 1, 1, 1, 9, 9, 1, 1 }, original);
            var rotated = atlas.FindRegion("rotated");
            var before = (rotated.degrees, rotated.offsetX, rotated.offsetY, rotated.originalWidth, rotated.originalHeight);
            var rotation = AtlasEditLayout.Resolve(atlas, Address("rotated"));
            Assert.Equal(2, rotation.Width); Assert.Equal(1, rotation.Height);
            rotation.CopyPixels(Enumerable.Repeat(5, 12).ToArray(), original);
            Assert.Equal(new[] { 1, 1, 1, 1, 5, 5, 1, 1, 9, 9, 1, 1 }, original);
            Assert.Equal(before, (rotated.degrees, rotated.offsetX, rotated.offsetY, rotated.originalWidth, rotated.originalHeight));
        }

        [Fact]
        public void Layout_RejectsUnknownRegionsOverlapAndWrongPixelCount()
        {
            var atlas = PortraitCatalog.ReadAtlas(AtlasText);
            Assert.Throws<InvalidDataException>(() => AtlasEditLayout.Resolve(atlas, Address("missing")));
            Assert.Throws<InvalidDataException>(() => AtlasEditLayout.Resolve(atlas, Address("missing.png", true)));
            var left = AtlasEditLayout.Resolve(atlas, Address("left"));
            var right = AtlasEditLayout.Resolve(atlas, Address("right"));
            AtlasEditLayout.ValidateEdits(new[] { left, right });
            Assert.Throws<InvalidDataException>(() => AtlasEditLayout.ValidateEdits(new[] { left, AtlasEditLayout.Resolve(atlas, Address("a.png", true)) }));
            Assert.Throws<InvalidDataException>(() => left.CopyPixels(new int[1], new int[12]));
            var alias = PortraitCatalog.ReadAtlas(AtlasText.Replace("right\nbounds: 2,0,2,1", "right\nbounds: 1,0,2,1"));
            Assert.Throws<InvalidDataException>(() => AtlasEditLayout.Resolve(alias, Address("left")));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void IndependentAtlasPack_ResolvesEncryptedImagesWithoutJson(bool encrypted)
        {
            using var pack = new EncryptedPackFixture();
            pack.Write("page.png", EncryptedPackFixture.Png);
            var address = Address("other");
            pack.WriteText("atlas.replacement.json", Manifest("atlas", address));
            string root = pack.Input;
            if (encrypted) { ToolEncryptor.Encrypt(pack.Input, pack.Output); root = pack.Output; }
            var catalog = pack.Discover(root: root);
            Assert.Empty(catalog.Errors);
            var target = Assert.Single(Assert.Single(catalog.Packages).Targets);
            Assert.Null(target.JsonPath); Assert.Null(target.AtlasPath);
            Assert.Equal(address.Identity, target.Identity);
            var image = ReplacementPreparation.Texture(target, root, Path.Combine(root, "Sensitive"), true, default);
            var layout = AtlasEditLayout.Resolve(PortraitCatalog.ReadAtlas(AtlasText), target.AtlasAddress);
            Assert.Equal("b.png", layout.Page.name);
            Assert.Equal(EncryptedPackFixture.Png, image);
        }

        [Fact]
        public void OriginalRotation_DoesNotBlockPixelOnlyPageReplacement()
        {
            // 独立原图集含 180/270 度区域；整页写入不重新解释骨架或改变其布局。
            var atlas = PortraitCatalog.ReadAtlas("a.png\nsize: 1,1\nfilter: Nearest,Nearest\nfoot\nbounds: 0,0,1,1\nrotate: 180\n");
            Assert.Throws<InvalidDataException>(() => SpinePageLayout.Validate(atlas));
            var page = AtlasEditLayout.Resolve(atlas, Address("a.png", true));
            PortraitCatalog.ValidatePageImage(EncryptedPackFixture.Png, page.Page);
            var original = new[] { 1 };
            page.CopyPixels(new[] { 9 }, original);
            Assert.Equal(new[] { 9 }, original);
            Assert.Equal(180, atlas.FindRegion("foot").degrees);
            Assert.Equal(1, AtlasEditLayout.Resolve(atlas, Address("foot")).Width);
            atlas.FindRegion("foot").x = 1;
            Assert.Throws<InvalidDataException>(() => AtlasEditLayout.Resolve(atlas, Address("foot")));
            Assert.Throws<InvalidDataException>(() => PortraitCatalog.ValidatePageImage(new byte[0], page.Page));
            page.Page.width = 2;
            Assert.Throws<InvalidDataException>(() => PortraitCatalog.ValidatePageImage(EncryptedPackFixture.Png, page.Page));
        }

        [Fact]
        public void Selection_LastPackWinsPerRegionAndAuthorizationRemovesEdits()
        {
            using var pack = new EncryptedPackFixture();
            pack.Write("page.png", EncryptedPackFixture.Png);
            pack.WriteText("a.replacement.json", Manifest("first", Address("left")));
            pack.WriteText("b.replacement.json", Manifest("second", Address("left")));
            var catalog = pack.Discover();
            var selection = new ReplacementSelection(catalog, new[] { "first", "second" }, true, true);
            Assert.Equal("second", Assert.Single(selection.AtlasTargets(Address("a.png", true))).PackageId);
            Assert.Empty(new ReplacementSelection(catalog, new[] { "first" }, false, true).AtlasTargets(Address("a.png", true)));
            Assert.Empty(selection.AtlasTargets(AtlasResourceAddress.Create("resources", null, "shared.atlas", "left")));
            Assert.NotEqual(Address("a.png", true).Identity, Address("a.png").Identity);
        }

        private static string Manifest(string id, AtlasResourceAddress address) => PortraitJson.Serialize(new Dictionary<string, object>
        {
            ["formatVersion"] = 2, ["id"] = id, ["targets"] = new[] { new Dictionary<string, object>
            { ["type"] = address.Kind, ["address"] = address.Describe(), ["image"] = "page.png" } }
        });
    }
}
