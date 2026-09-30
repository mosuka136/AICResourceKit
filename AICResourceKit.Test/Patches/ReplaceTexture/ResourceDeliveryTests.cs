extern alias ResourceEncryptor;

using AICResourceKit.Contracts;
using AICResourceKit.Patches.ReplaceTexture;
using System.Text.Json;
using ToolCommand = ResourceEncryptor::AICResourceKit.ResourceEncryptor.Program;
using ToolInventory = ResourceEncryptor::AICResourceKit.ResourceEncryptor.PackInventory;
using ToolEncryptor = ResourceEncryptor::AICResourceKit.ResourceEncryptor.PackEncryptor;

namespace AICResourceKit.Test.Patches.ReplaceTexture
{
    public sealed class ResourceDeliveryTests
    {
        [Fact]
        public void Capabilities_MatchDeliveredContractAndDoNotClaimRuntimeVerification()
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, ToolCommand.Run(new[] { "capabilities" }, output, error));
            Assert.Equal("", error.ToString());
            Assert.Equal(PortraitJson.Serialize(ResourceCapabilities.Describe()), PortraitJson.Serialize(PortraitJson.Parse(output.ToString())));
            string delivered = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "capabilities.json"));
            Assert.Equal(PortraitJson.Serialize(ResourceCapabilities.Describe()), PortraitJson.Serialize(PortraitJson.Parse(delivered)));
            using var json = JsonDocument.Parse(output.ToString());
            var root = json.RootElement;
            Assert.Equal("stable", root.GetProperty("contractStatus").GetString());
            Assert.Equal(PatchInfo.BepInPluginVersion, root.GetProperty("pluginVersion").GetString());
            Assert.Equal(ResourceCapabilities.AssemblyVersion, typeof(ResourceManifest).Assembly.GetName().Version.ToString());
            Assert.Equal("not-performed-by-this-report", root.GetProperty("runtimeValidation").GetString());
            var video = root.GetProperty("features").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "video");
            Assert.False(video.GetProperty("supported").GetBoolean());
            Assert.False(video.GetProperty("installableTarget").GetBoolean());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MixedPack_InspectionEncryptionAndRuntimePreparationAgree(bool encryptedInput)
        {
            using var pack = new EncryptedPackFixture();
            pack.Write("shared/page.png", EncryptedPackFixture.Png, encryptedInput);
            pack.Write("shared/second.png", EncryptedPackFixture.Png, encryptedInput);
            pack.WriteText("shared/two.atlas", EncryptedPackFixture.Atlas + "\n" + EncryptedPackFixture.Atlas.Replace("page.png", "second.png").Replace("part", "second"), encryptedInput);
            pack.WriteText("shared/page.json", EncryptedPackFixture.Skeleton, encryptedInput);
            pack.WriteText("normal/pack.replacement.json", MixedManifest, encryptedInput);
            pack.WriteText("override/pack.replacement.json", """
                {"formatVersion":2,"id":"override","targets":[
                {"type":"texture","loader":"mti","assetKey":"UI/Sheet","image":"../shared/page.png"}]}
                """);
            pack.WriteText("shared/source.psd", "unreferenced source file");
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, ToolCommand.Run(new[] { "inspect", "--input", pack.Input }, output, error));
            Assert.Equal("", error.ToString());
            Assert.False(Directory.Exists(pack.Output));
            using var report = JsonDocument.Parse(output.ToString());
            Assert.Equal(6, report.RootElement.GetProperty("fileCount").GetInt32());
            Assert.DoesNotContain(pack.DirectoryPath, output.ToString());
            Assert.DoesNotContain("source.psd", output.ToString());
            Assert.Equal(6, ToolEncryptor.Encrypt(pack.Input, pack.Output));
            Assert.Equal(output.ToString(), JsonSerializer.Serialize(ToolInventory.Read(pack.Output).Describe(), new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            var catalog = pack.Discover(root: pack.Output);
            Assert.Empty(catalog.Errors);
            var mixed = catalog.Packages.Single(item => item.Id == "mixed");
            Assert.Equal(9, mixed.Targets.Count);
            string sensitive = Path.Combine(pack.Output, "Sensitive");
            foreach (var target in mixed.Targets.Where(item => item.ImagePath != null))
                Assert.Equal(EncryptedPackFixture.Png, ReplacementPreparation.Texture(target, pack.Output, sensitive, true, default));
            var ordinary = mixed.Targets.Single(item => item.Type == "spine-assets");
            var prepared = ReplacementPreparation.Spine(EncryptedPackFixture.Skeleton, EncryptedPackFixture.Atlas, 1,
                new[] { ordinary }, pack.Output, sensitive, true, default, multiplePages: true);
            Assert.Equal(new[] { "page.png", "second.png" }, prepared.PageBytes.Keys.OrderBy(key => key));
            Assert.All(prepared.PageBytes.Values, bytes => Assert.Equal(EncryptedPackFixture.Png, bytes));
            Assert.Contains(mixed.Targets, target => target.Identity == ResourceIdentity.Spine("stand_battle", "stand_battle.old"));
            var selection = new ReplacementSelection(catalog, new[] { "mixed", "override" }, true, true);
            Assert.Equal("override", selection.Texture("mti", "UI/Sheet", "any-image", null).PackageId);
        }

        [Fact]
        public void Inspect_IdentifiesMissingPageWithoutPublishingOutput()
        {
            using var pack = new EncryptedPackFixture();
            pack.WriteText("multi/pack.replacement.json", """
                {"formatVersion":2,"id":"broken","targets":[
                {"type":"spine-assets","address":{"kind":"spine-assets","loader":"resources","atlasKey":"UI/atlas","jsonKey":"UI/body"},
                "pages":[{"pageKey":"page.png","image":"missing.png"}]}]}
                """);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(1, ToolCommand.Run(new[] { "inspect", "--input", pack.Input }, output, error));
            Assert.Equal("", output.ToString());
            Assert.Contains("multi/pack.replacement.json", error.ToString());
            Assert.Contains("targets[0] missing.png", error.ToString());
            pack.AssertNoOutput();
        }

        [Fact]
        public void StandaloneTitleSample_UsesVerifiedAddressAndEncryptsWithoutSourceArt()
        {
            using var pack = new EncryptedPackFixture();
            string sample = Path.Combine(AppContext.BaseDirectory, "Fixtures", "title-checker");
            foreach (string file in Directory.GetFiles(sample)) pack.Write(Path.GetFileName(file), File.ReadAllBytes(file));
            var target = Assert.Single(Assert.Single(pack.Discover().Packages).Targets);
            Assert.Equal(ResourceIdentity.Mti("MTI_title", "key_noel"), target.Identity);
            var png = ReplacementResourceIO.ReadBytes(target.ImagePath);
            Assert.Equal(1280, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
            Assert.Equal(1520, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
            Assert.Equal(2, ToolEncryptor.Encrypt(pack.Input, pack.Output));
            Assert.Empty(pack.Discover(root: pack.Output).Errors);
        }

        private const string MixedManifest = """
            {"formatVersion":2,"id":"mixed","targets":[
            {"type":"texture","loader":"mti","assetKey":"UI/Sheet","image":"../shared/page.png"},
            {"type":"texture","loader":"resources","path":"UI/Icon","objectType":"Texture2D","image":"../shared/page.png"},
            {"type":"texture","loader":"resources","path":"UI/Icon","objectType":"Sprite","image":"../shared/page.png"},
            {"type":"texture","loader":"pxl","address":{"kind":"pxl-page","source":{"loader":"mti","assetKey":"Pxl/test","textKey":"test.pxls"},"storage":"external","pageIndex":0},"image":"../shared/page.png"},
            {"type":"texture","loader":"pxl","address":{"kind":"pxl-image","source":{"loader":"mti","assetKey":"Pxl/test","textKey":"test.pxls"},"imageId":"7","imageId2":"3.5","role":"I"},"image":"../shared/page.png"},
            {"type":"spine","key":"stand_battle","jsonKey":"stand_battle.old","spine":{"json":"../shared/page.json","replace":["all"]}},
            {"type":"spine-assets","address":{"kind":"spine-assets","loader":"resources","atlasKey":"UI/atlas","jsonKey":"UI/body"},"atlas":"../shared/two.atlas",
             "pages":[{"pageKey":"page.png","image":"../shared/page.png"},{"pageKey":"second.png","image":"../shared/second.png"}]},
            {"type":"atlas-region","address":{"kind":"atlas-region","loader":"resources","atlasKey":"UI/picture","region":"part"},"image":"../shared/page.png"},
            {"type":"atlas-page","address":{"kind":"atlas-page","loader":"resources","atlasKey":"UI/picture2","pageKey":"page.png"},"image":"../shared/page.png"}]}
            """;
    }
}
