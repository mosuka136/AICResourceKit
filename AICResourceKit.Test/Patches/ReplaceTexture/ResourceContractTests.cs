extern alias ResourceEncryptor;

using AICResourceKit.Contracts;
using AICResourceKit.Patches.ReplaceTexture;
using System.Text.Json;
using ToolManifest = ResourceEncryptor::AICResourceKit.Contracts.ResourceManifest;
using ToolJson = ResourceEncryptor::AICResourceKit.ResourceEncryptor.ManifestJson;
using ToolAddress = ResourceEncryptor::AICResourceKit.Contracts.ResourceAddressDraft;
using ToolEncryptor = ResourceEncryptor::AICResourceKit.ResourceEncryptor.PackEncryptor;

namespace AICResourceKit.Test.Patches.ReplaceTexture
{
    public sealed class ResourceContractTests
    {
        public static IEnumerable<object[]> Vectors()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "contract-vectors.json")));
            foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray())
                yield return new object[] { item.GetProperty("name").GetString(), item.GetRawText() };
        }

        [Theory]
        [MemberData(nameof(Vectors))]
        public void ContractVectors_AgreeBetweenPluginAndAuthorTool(string name, string vector)
        {
            using var document = JsonDocument.Parse(vector);
            var test = document.RootElement;
            string json = test.GetProperty("manifest").GetRawText();
            if (!test.GetProperty("valid").GetBoolean())
            {
                Assert.Throws<InvalidDataException>(() => ResourceManifest.Parse(PortraitJson.Parse(json)));
                Assert.Throws<InvalidDataException>(() => ToolManifest.Parse(ToolJson.Parse(json)));
                return;
            }
            var plugin = ResourceManifest.Parse(PortraitJson.Parse(json));
            var tool = ToolManifest.Parse(ToolJson.Parse(json));
            string[] identities = test.GetProperty("identities").EnumerateArray().Select(item => item.GetString()).ToArray();
            string[] dependencies = test.GetProperty("dependencies").EnumerateArray().Select(item => item.GetString()).ToArray();
            Assert.True(identities.SequenceEqual(plugin.Targets.Select(target => target.Identity)), name + ": plugin identities");
            Assert.Equal(identities, tool.Targets.Select(target => target.Identity));
            Assert.Equal(dependencies, plugin.Targets.SelectMany(target => target.Dependencies).Select(item => item.Kind + ":" + item.Path));
            Assert.Equal(dependencies, tool.Targets.SelectMany(target => target.Dependencies).Select(item => item.Kind + ":" + item.Path));
        }

        [Theory]
        [InlineData(null, null, true)]
        [InlineData(null, "", true)]
        [InlineData(null, "page", true)]
        [InlineData("", null, false)]
        [InlineData("", "", true)]
        [InlineData("", "page", false)]
        [InlineData("page", "page", true)]
        [InlineData("page", "Page", false)]
        public void MtiMatching_PreservesLegacyNullWildcardAndExactEmptyKey(string configured, string actual, bool expected)
        {
            Assert.Equal(expected, ResourceIdentity.MatchesMti("A", configured, "A", actual));
            Assert.False(ResourceIdentity.MatchesMti("A", configured, "B", actual));
            Assert.Equal(ResourceIdentity.Mti("A", null), ResourceIdentity.Mti("A", ""));
        }

        [Theory]
        [InlineData("null")]
        [InlineData("42")]
        [InlineData("{\"type\":\"video\"}")]
        public void Runtime_IsolatesMalformedOrUnknownTargetAndRetainsValidNeighbor(string badTarget)
        {
            using var pack = new EncryptedPackFixture();
            pack.WriteText("pack.replacement.json", "{\"formatVersion\":2,\"id\":\"mixed\",\"targets\":[" + badTarget
                + ", {\"type\":\"spine\",\"key\":\"stand_battle\",\"jsonKey\":\"stand_battle.old\",\"display\":{\"offsetX\":1}}]}");
            var catalog = pack.Discover();
            var package = Assert.Single(catalog.Packages);
            Assert.True(package.HasUnidentifiedTargetErrors);
            Assert.Equal(ResourceIdentity.Spine("stand_battle", "stand_battle.old"), Assert.Single(package.Targets).Identity);
            Assert.Single(catalog.Errors);
            Assert.Throws<InvalidDataException>(() => ToolEncryptor.Encrypt(pack.Input, pack.Output));
            pack.AssertNoOutput();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void V2Atlas_RejectsMissingDependencyOrMultiplePages(bool multiplePages)
        {
            using var pack = new EncryptedPackFixture();
            pack.WriteText("pack.replacement.json", """
                {"formatVersion":2,"id":"atlas","targets":[
                {"type":"spine","key":"sample","jsonKey":"sample","atlas":"page.atlas"}]}
                """);
            if (multiplePages) pack.WriteText("page.atlas", EncryptedPackFixture.Atlas + "\n" + EncryptedPackFixture.Atlas.Replace("page.png", "second.png"));
            var catalog = pack.Discover();
            var package = Assert.Single(catalog.Packages);
            Assert.Empty(package.Targets);
            Assert.Contains(ResourceIdentity.Spine("sample", "sample"), package.InvalidTargetIdentities);
            Assert.Single(catalog.Errors);
            Assert.Contains(multiplePages ? "exactly one page" : "not found", catalog.Errors[0]);
        }

        [Fact]
        public void AuthorTool_RejectsMalformedSpineSectionsBeforeCreatingOutput()
        {
            using var pack = new EncryptedPackFixture();
            pack.CreatePack();
            pack.WriteText("pack.replacement.json", EncryptedPackFixture.Manifest("bad").Replace("[\"all\"]", "[\"all\",\"bones\"]"));
            Assert.Throws<InvalidDataException>(() => ToolEncryptor.Encrypt(pack.Input, pack.Output));
            pack.AssertNoOutput();
        }

        [Fact]
        public void DraftExamples_ProduceSameAddressesInBothHostsButAreNotInstallablePackages()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "address-draft.examples.json")));
            foreach (var item in document.RootElement.GetProperty("examples").EnumerateArray())
            {
                string json = item.GetProperty("address").GetRawText();
                string identity = ResourceAddressDraft.IdentityOf(PortraitJson.Parse(json));
                Assert.Equal(identity, ToolAddress.IdentityOf(ToolJson.Parse(json)));
                Assert.StartsWith("draft1|", identity);
                Assert.Throws<InvalidDataException>(() => ResourceManifest.ReadTarget(PortraitJson.Parse(json)));
            }
        }

        [Fact]
        public void DraftSpine_SeparatesContainerJsonAndLoader()
        {
            var address = PortraitJson.Parse("""
                {"kind":"spine-assets","loader":"mti","assetKey":"Fatal/shared.atlas","atlasKey":"shared","jsonKey":"one"}
                """);
            string first = ResourceAddressDraft.IdentityOf(address);
            address["jsonKey"] = "two";
            Assert.NotEqual(first, ResourceAddressDraft.IdentityOf(address));
            address["jsonKey"] = "one";
            address["assetKey"] = "Other/shared.atlas";
            Assert.NotEqual(first, ResourceAddressDraft.IdentityOf(address));
            address.Remove("assetKey");
            address["loader"] = "resources";
            Assert.NotEqual(first, ResourceAddressDraft.IdentityOf(address));
        }

        private static Dictionary<string, object> Pxl() => PortraitJson.Parse("""
            {"kind":"pxl-image","source":{"loader":"mti","assetKey":"Pxl/sample.pxls","textKey":"sample.pxls"},
             "imageId":"4294967295","imageId2":"0.5","role":"I"}
            """);

        [Fact]
        public void DraftPxl_UsesOriginalIdsWithBinary64PrecisionAndScopedSource()
        {
            var address = Pxl();
            string first = ResourceAddressDraft.IdentityOf(address);
            address["imageId2"] = "0.5000000000000001";
            Assert.NotEqual(first, ResourceAddressDraft.IdentityOf(address));
            address["imageId2"] = "5e-1";
            Assert.Equal(first, ResourceAddressDraft.IdentityOf(address));
            address["role"] = "P";
            Assert.NotEqual(first, ResourceAddressDraft.IdentityOf(address));
            address["role"] = "I";
            PortraitJson.Object(address["source"])["assetKey"] = "Pxl/other.pxls";
            Assert.NotEqual(first, ResourceAddressDraft.IdentityOf(address));
        }

        [Theory]
        [InlineData("imageId", "4294967296")]
        [InlineData("imageId", "-1")]
        [InlineData("imageId2", "NaN")]
        [InlineData("imageId2", "Infinity")]
        [InlineData("imageId2", "1e400")]
        [InlineData("role", "mask")]
        [InlineData("instanceId", "123")]
        [InlineData("sourceObjectId", "exported-object")]
        [InlineData("taskId", "wardrobe-job")]
        public void DraftPxl_RejectsInvalidOrUnrelatedIdentifiers(string field, string value)
        {
            var address = Pxl();
            address[field] = value;
            Assert.Throws<InvalidDataException>(() => ResourceAddressDraft.IdentityOf(address));
        }

        [Theory]
        [InlineData("D:/Game/StreamingAssets/file")]
        [InlineData("../export/file")]
        [InlineData("/absolute/file")]
        public void DraftSource_RejectsFilesystemLocationAsRuntimeIdentity(string key)
        {
            var address = Pxl();
            PortraitJson.Object(address["source"])["assetKey"] = key;
            Assert.Throws<InvalidDataException>(() => ResourceAddressDraft.IdentityOf(address));
        }

        [Fact]
        public void DraftPages_RequireCompleteExactKeysAndAllowExplicitSharedImages()
        {
            var expected = new[] { "PageA", "pageB" };
            var complete = new[] { new ResourcePageDraft("pageB", "shared.png"), new ResourcePageDraft("PageA", "shared.png") };
            Assert.Equal(new[] { "shared.png", "shared.png" }, ResourceAddressDraft.ValidatePages(expected, complete).Select(item => item.Path));
            Assert.Throws<InvalidDataException>(() => ResourceAddressDraft.ValidatePages(expected, complete.Take(1)));
            Assert.Throws<InvalidDataException>(() => ResourceAddressDraft.ValidatePages(expected, complete.Append(complete[0])));
            Assert.Throws<InvalidDataException>(() => ResourceAddressDraft.ValidatePages(expected, new[] { new ResourcePageDraft("pagea", "a.png"), complete[0] }));
            Assert.Throws<InvalidDataException>(() => ResourceAddressDraft.ValidatePages(new[] { "a", "a" }, complete));
            Assert.Throws<InvalidDataException>(() => ResourceAddressDraft.ValidatePages(Array.Empty<string>(), Array.Empty<ResourcePageDraft>()));
            Assert.Throws<InvalidDataException>(() => ResourceAddressDraft.ValidatePages(null, complete));
            Assert.Throws<InvalidDataException>(() => ResourceAddressDraft.ValidatePages(new[] { "a" }, new[] { new ResourcePageDraft("a", "D:/a.png") }));
        }

        [Fact]
        public void MpccIdentity_RemainsUnspecifiedUntilSourceBindingIsKnown()
        {
            Assert.Throws<InvalidDataException>(() => ResourceAddressDraft.IdentityOf(PortraitJson.Parse(
                "{\"kind\":\"mpcc\",\"name\":\"sample\",\"chr_name\":\"noel\"}")));
        }
    }
}
