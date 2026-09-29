extern alias ResourceEncryptor;

using AICResourceKit.Contracts;
using AICResourceKit.Patches.ReplaceTexture;
using System.Globalization;
using ToolManifest = ResourceEncryptor::AICResourceKit.Contracts.ResourceManifest;
using ToolJson = ResourceEncryptor::AICResourceKit.ResourceEncryptor.ManifestJson;
using ToolEncryptor = ResourceEncryptor::AICResourceKit.ResourceEncryptor.PackEncryptor;

namespace AICResourceKit.Test.Patches.ReplaceTexture
{
    public sealed class PxlReplacementTests
    {
        private static string Pack(params PxlResourceAddress[] addresses) => PortraitJson.Serialize(new Dictionary<string, object>
        {
            ["formatVersion"] = 2, ["id"] = "pxl-sample", ["targets"] = addresses.Select(address => new Dictionary<string, object>
            { ["type"] = "texture", ["loader"] = "pxl", ["address"] = address.Describe(), ["image"] = "page.png" }).ToArray()
        });

        [Fact]
        public void MixedPages_KeepSourceImageRolesAndPageKindsDistinctInPluginAndTool()
        {
            var addresses = new[]
            {
                PxlResourceAddress.Embedded("Pxl/noel.pxls", "noel.pxls", 12, 3.5, "I"),
                PxlResourceAddress.Embedded("Pxl/noel.pxls", "noel.pxls", 12, 3.5, "P"),
                PxlResourceAddress.Page("Pxl/noel.pxls", "noel.pxls", 0),
                PxlResourceAddress.Page("Pxl/noel.pxls", "noel.pxls", 1),
                PxlResourceAddress.Page("Pxl/noel.pxls", "noel.pxls", 1, 1)
            };
            string json = Pack(addresses);
            var plugin = ResourceManifest.Parse(PortraitJson.Parse(json));
            var tool = ToolManifest.Parse(ToolJson.Parse(json));
            Assert.Equal(5, plugin.Targets.Select(target => target.Identity).Distinct().Count());
            Assert.Equal(plugin.Targets.Select(target => target.Identity), tool.Targets.Select(target => target.Identity));
            Assert.All(plugin.Targets, target => Assert.Equal("page.png", Assert.Single(target.Dependencies).Path));
            Assert.NotEqual(addresses[0].Identity, PxlResourceAddress.Embedded("Other/noel.pxls", "noel.pxls", 12, 3.5, "I").Identity);
        }

        [Fact]
        public void EmbeddedId_RoundTripsBinary64AcrossCultures()
        {
            var before = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                var address = PxlResourceAddress.Embedded("A", "B", uint.MaxValue, 123.45678901234567, "I");
                var copy = PxlResourceAddress.Parse(address.Describe());
                Assert.Equal(BitConverter.DoubleToInt64Bits(address.ImageId2), BitConverter.DoubleToInt64Bits(copy.ImageId2));
                Assert.Equal(address.Identity, copy.Identity);
                Assert.NotEqual(address.Identity, PxlResourceAddress.Embedded("A", "B", uint.MaxValue, 123.45678901234568, "I").Identity);
            }
            finally { CultureInfo.CurrentCulture = before; }
        }

        [Theory]
        [InlineData("imageId", "4294967296")]
        [InlineData("imageId2", "NaN")]
        [InlineData("imageId2", "1e999")]
        [InlineData("role", "mask")]
        [InlineData("pageIndex", "0")]
        public void EmbeddedAddress_RejectsBadIdsAndUnrelatedFields(string field, string value)
        {
            var address = PxlResourceAddress.Embedded("A", "B", 1, 2, "I").Describe();
            address[field] = value;
            Assert.Throws<InvalidDataException>(() => PxlResourceAddress.Parse(address));
        }

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(0, 3)]
        public void PackedAddress_RejectsInvalidPageAndRole(int index, int type) =>
            Assert.Throws<InvalidDataException>(() => PxlResourceAddress.Page("A", "B", index, type));

        [Fact]
        public void SameTextureAliases_UsePackOrderAndRejectAmbiguousTargetsWithinOnePack()
        {
            var external = PxlResourceAddress.Page("A", "B", 0);
            var packed = PxlResourceAddress.Page("A", "B", 0, 0);
            var first = new ReplacementPackage { Id = "first" };
            var last = new ReplacementPackage { Id = "last", Sensitive = true };
            var a = Target(first, external);
            var b = Target(last, packed);
            var catalog = new ReplacementCatalog();
            catalog.Packages.AddRange(new[] { first, last });
            var aliases = new HashSet<string> { external.Identity, packed.Identity };
            Assert.Same(b, new ReplacementSelection(catalog, new[] { "first", "last" }, true, true).Pxl(aliases));
            Assert.Same(a, new ReplacementSelection(catalog, new[] { "first", "last" }, true, false).Pxl(aliases));
            Assert.Null(new ReplacementSelection(catalog, new[] { "first", "last" }, false, true).Pxl(aliases));
            Target(first, packed);
            Assert.Throws<InvalidOperationException>(() => new ReplacementSelection(catalog, new[] { "first" }, true, true).Pxl(aliases));
        }

        [Fact]
        public void InvalidPageDependency_DoesNotHideAValidNeighborOrPassStrictExport()
        {
            using var fixture = new EncryptedPackFixture();
            var addresses = new[] { PxlResourceAddress.Page("A", "B", 0), PxlResourceAddress.Page("A", "B", 1) };
            var json = PortraitJson.Parse(Pack(addresses));
            PortraitJson.Object(PortraitJson.Array(json["targets"])[1])["image"] = "missing.png";
            fixture.WriteText("pack.replacement.json", PortraitJson.Serialize(json));
            fixture.Write("page.png", EncryptedPackFixture.Png);
            var catalog = fixture.Discover();
            var package = Assert.Single(catalog.Packages);
            Assert.Equal(addresses[0].Identity, Assert.Single(package.Targets).Identity);
            Assert.Contains(addresses[1].Identity, package.InvalidTargetIdentities);
            Assert.Single(catalog.Errors);
            Assert.ThrowsAny<IOException>(() => ToolEncryptor.Encrypt(fixture.Input, fixture.Output));
            fixture.AssertNoOutput();
        }

        [Fact]
        public void PxlPack_EncryptedDependenciesRemainDiscoverable()
        {
            using var fixture = new EncryptedPackFixture();
            fixture.WriteText("pack.replacement.json", Pack(PxlResourceAddress.Page("A", "B", 0)));
            fixture.Write("page.png", EncryptedPackFixture.Png);
            ToolEncryptor.Encrypt(fixture.Input, fixture.Output);
            var catalog = fixture.Discover(root: fixture.Output);
            Assert.Empty(catalog.Errors);
            var target = Assert.Single(Assert.Single(catalog.Packages).Targets);
            Assert.Equal(EncryptedPackFixture.Png, ReplacementResourceIO.ReadBytes(target.ImagePath));
        }

        [Fact]
        public void SourceTracking_UsesLoadedObjectsAndKeepsSameNamedSourcesSeparate()
        {
            var first = new byte[] { 1 };
            var second = new byte[] { 1 };
            var source = new PxlSourceKey("A", "same.pxls");
            ReplacementPxlSource.Remember(first, source);
            Assert.Same(source, ReplacementPxlSource.Find(first));
            Assert.Null(ReplacementPxlSource.Find(second));
            ReplacementPxlSource.Remember(second, new PxlSourceKey("B", "same.pxls"));
            Assert.Equal("B", ReplacementPxlSource.Find(second).AssetKey);
        }

        [Theory]
        [InlineData("same.pxls", "same.pxls")]
        [InlineData("Assets/Editor/AssetBundlesSrc/A/folder/same.pxls", "folder/same.pxls")]
        public void TextSource_UsesRegisteredBundleAndOriginalMtiKey(string path, string key)
        {
            var bundle = new object();
            var text = new object();
            ReplacementPxlSource.LoadedText(bundle, path, text);
            Assert.Null(ReplacementPxlSource.Find(text));
            ReplacementPxlSource.Remember(bundle, new PxlSourceKey("A", null));
            ReplacementPxlSource.LoadedText(bundle, path, text);
            Assert.Equal("A", ReplacementPxlSource.Find(text).AssetKey);
            Assert.Equal(key, ReplacementPxlSource.Find(text).TextKey);
        }

        private static ReplacementTarget Target(ReplacementPackage package, PxlResourceAddress address)
        {
            var target = new ReplacementTarget { Type = "texture", Loader = "pxl", PxlAddress = address, Owner = package, PackageId = package.Id };
            package.Targets.Add(target);
            return target;
        }
    }
}
