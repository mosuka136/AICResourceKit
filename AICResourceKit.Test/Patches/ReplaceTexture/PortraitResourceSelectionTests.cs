extern alias ResourceEncryptor;

using AICResourceKit.Contracts;
using AICResourceKit.Patches.ReplaceTexture;
using Spine;
using ToolEncryptor = ResourceEncryptor::AICResourceKit.ResourceEncryptor.PackEncryptor;

namespace AICResourceKit.Test.Patches.ReplaceTexture
{
    public sealed class PortraitResourceSelectionTests
    {
        private static PortraitResourceSelection Select(string json) => PortraitResourceSelection.Parse(PortraitJson.Parse(json), true);

        [Theory]
        [InlineData("STAND", 0u, "weak", true)]
        [InlineData("STAND", 32u, "weak", true)]
        [InlineData("STAND", 8192u, "weak", true)]
        [InlineData("STAND", 1048608u, "weak", false)]
        [InlineData("DAMAGE", 0u, "weak", false)]
        [InlineData("STAND", 0u, "stand", false)]
        [InlineData(null, 0u, null, false)]
        public void Conditions_ReplaceWeakOnlyWithoutTornClothes(string pose, uint state, string animation, bool expected)
        {
            var filter = Select("""{"poses":["STAND"],"animations":["weak"],"excludeStates":["TORNED"]}""");
            Assert.Equal(expected, filter.Matches(pose, state, animation));
        }

        [Fact]
        public void States_RequireAllFlagsAndNormalMeansZero()
        {
            var flags = Select("""{"requireStates":["LOWHP","LOWMP"]}""");
            Assert.False(flags.Matches("STAND", 32));
            Assert.True(flags.Matches("STAND", 32 | 8192 | 1048576));
            var normal = Select("""{"requireStates":["NORMAL"]}""");
            Assert.True(normal.Matches("STAND", 0));
            Assert.False(normal.Matches("STAND", 32));
        }

        [Fact]
        public void Pxl_ConditionalTargetNeverChangesSharedPageAndRespectsPackOrder()
        {
            var address = PxlResourceAddress.Page("Pxl/portrait.pxls", "portrait.pxls", 0);
            var catalog = new ReplacementCatalog();
            ReplacementTarget Add(string id, bool conditional)
            {
                var package = new ReplacementPackage { Id = id };
                var target = new ReplacementTarget { Owner = package, PackageId = id, Type = "texture", Loader = "pxl", PxlAddress = address,
                    PortraitSelection = conditional ? Select("""{"excludeStates":["TORNED"]}""") : null };
                package.Targets.Add(target); catalog.Packages.Add(package); return target;
            }
            var fallback = Add("full", false);
            var normal = Add("normal", true);
            var ids = new HashSet<string> { address.Identity };
            var selection = new ReplacementSelection(catalog, new[] { "full", "normal" }, true, true);
            Assert.Same(fallback, selection.Pxl(ids));
            Assert.Same(normal, selection.Pxl(ids, t => t.PortraitSelection == null || t.PortraitSelection.Matches("STAND", 0)));
            Assert.Same(fallback, selection.Pxl(ids, t => t.PortraitSelection == null || t.PortraitSelection.Matches("STAND", 1048576)));
            Assert.Null(new ReplacementSelection(catalog, new[] { "normal" }, true, true).Pxl(ids));
            Assert.Same(fallback, new ReplacementSelection(catalog, new[] { "normal", "full" }, true, true)
                .Pxl(ids, t => true));
        }

        [Fact]
        public void Spine_PartialStateExportDoesNotNeedInactiveTornAnimationOrSkin()
        {
            using var pack = new EncryptedPackFixture();
            pack.CreatePack();
            var manifest = PortraitJson.Parse(EncryptedPackFixture.Manifest("normal"));
            manifest["formatVersion"] = 3;
            var spine = PortraitJson.Object(PortraitJson.Array(manifest["targets"]).Last());
            spine["portraitSelection"] = PortraitJson.Parse("""{"excludeStates":["TORNED"]}""");
            pack.WriteText("pack.replacement.json", PortraitJson.Serialize(manifest));
            var catalog = pack.Discover();
            var target = catalog.Packages.Single().Targets.Single(t => t.Type == "spine");
            var original = PortraitJson.Parse(EncryptedPackFixture.Skeleton);
            PortraitJson.Object(original["animations"])["torned"] = new Dictionary<string, object>();
            PortraitJson.Array(original["skins"]).Add(PortraitJson.Parse("""{"name":"torn-only","attachments":{}}"""));
            var atlas = new Atlas(new StringReader(EncryptedPackFixture.Atlas), "", new EmptyTextureLoader());
            var composition = SpineComposer.Compose(PortraitJson.Serialize(original), new[] { target }, atlas, 1f);
            Assert.NotNull(composition.PreparedData.FindAnimation("stand"));
            Assert.Null(composition.PreparedData.FindAnimation("torned"));
            Assert.Null(composition.PreparedData.FindSkin("torn-only"));
            ReplacementRuntime.ValidatePortraitData(composition.PreparedData, new[] { "stand" }, new[] { "default" });
            Assert.Throws<InvalidDataException>(() => ReplacementRuntime.ValidatePortraitData(composition.PreparedData, new[] { "torned" }, new[] { "default" }));
            Assert.Throws<InvalidDataException>(() => ReplacementRuntime.ValidatePortraitData(composition.PreparedData, new[] { "stand" }, new[] { "absent" }));
            ToolEncryptor.Encrypt(pack.Input, pack.Output);
            var encrypted = pack.Discover(root: pack.Output).Packages.Single().Targets.Single(t => t.Type == "spine");
            Assert.True(encrypted.PortraitSelection.Matches("STAND", 32));
            Assert.False(encrypted.PortraitSelection.Matches("STAND", 1048576));
            Assert.Empty(ReplacementPreview.Layers(new ReplacementSelection(catalog, new[] { "normal" }, true, true), target));
        }

        private sealed class EmptyTextureLoader : TextureLoader
        {
            public void Load(AtlasPage page, string path) { }
            public void Unload(object texture) { }
        }
    }
}
