extern alias ResourceEncryptor;

using AICResourceKit.Contracts;
using AICResourceKit.Patches.ReplaceTexture;
using Spine;
using ToolEncryptor = ResourceEncryptor::AICResourceKit.ResourceEncryptor.PackEncryptor;

namespace AICResourceKit.Test.Patches.ReplaceTexture
{
    public sealed class SpineAssetsTests
    {
        private const string Atlas = "a.png\nsize: 1,1\nfilter: Linear,Linear\npart\nbounds: 0,0,1,1\n\nb.png\nsize: 1,1\nfilter: Nearest,Nearest\nextra\nbounds: 0,0,1,1\n";
        private static SpineResourceAddress Address(string json = "fatal_nusi_1") =>
            SpineResourceAddress.Create("mti", "Fatal/fatal_nusi_0.atlas", "fatal_nusi_0.atlas", json);

        private static string Manifest(params string[] jsonKeys) => PortraitJson.Serialize(new Dictionary<string, object>
        {
            ["formatVersion"] = 2, ["id"] = "viewer", ["targets"] = jsonKeys.Select(key => new Dictionary<string, object>
            {
                ["type"] = "spine-assets", ["address"] = Address(key).Describe(), ["atlas"] = "two.atlas",
                ["pages"] = new[] { new { pageKey = "a.png", image = "a.png" }, new { pageKey = "b.png", image = "b.png" } }
                    .Select(page => new Dictionary<string, object> { ["pageKey"] = page.pageKey, ["image"] = page.image }).ToArray()
            }).ToArray()
        });

        [Fact]
        public void Addresses_SeparateSharedAtlasVariantsLoadersAndPortraits()
        {
            var first = Address("fatal_nusi_0");
            var second = Address();
            Assert.NotEqual(first.Identity, second.Identity);
            Assert.NotEqual(second.Identity, ResourceIdentity.Spine("fatal_nusi_0", "fatal_nusi_1"));
            Assert.NotEqual(second.Identity, SpineResourceAddress.Create("resources", null, "fatal_nusi_0.atlas", "fatal_nusi_1").Identity);
            Assert.Equal(second.Identity, SpineResourceAddress.Parse(second.Describe()).Identity);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MultiPagePreparation_ResolvesEveryPageAndBothJsonVariants(bool encrypted)
        {
            using var pack = CreatePack();
            string root = pack.Input;
            if (encrypted) { ToolEncryptor.Encrypt(pack.Input, pack.Output); root = pack.Output; }
            var catalog = pack.Discover(root: root);
            Assert.Empty(catalog.Errors);
            var targets = Assert.Single(catalog.Packages).Targets;
            Assert.Equal(2, targets.Count);
            foreach (var target in targets)
            {
                Assert.Equal(new[] { "a.png", "b.png" }, target.Pages.Select(page => page.PageKey));
                var prepared = Prepare(target, root);
                Assert.Equal(new[] { "a.png", "b.png" }, prepared.Atlas.Pages.Select(page => page.name));
                Assert.Equal(EncryptedPackFixture.Png, prepared.PageBytes["b.png"]);
                var attachment = Assert.IsType<RegionAttachment>(prepared.Composition.PreparedData.DefaultSkin.GetAttachment(0, "part"));
                Assert.Same(prepared.Atlas.Pages[0], Assert.IsType<AtlasRegion>(attachment.Region).page);
                Assert.NotNull(prepared.Composition.PreparedData.FindAnimation("stand"));
            }
        }

        [Theory]
        [InlineData("missing")]
        [InlineData("unknown")]
        [InlineData("dimensions")]
        [InlineData("duplicate-region")]
        public void MultiPagePreparation_RejectsIncompleteOrMismatchedLayouts(string problem)
        {
            using var pack = CreatePack();
            var target = pack.Discover().Packages[0].Targets[0];
            if (problem == "missing") { target.Pages.RemoveAt(1); target.PagePaths.Remove("b.png"); }
            if (problem == "unknown") target.Pages[1] = new ResourcePageDraft("c.png", "b.png");
            if (problem == "dimensions") pack.WriteText("two.atlas", Atlas.Replace("size: 1,1", "size: 2,2"));
            if (problem == "duplicate-region") pack.WriteText("two.atlas", Atlas.Replace("extra", "part"));
            Assert.Throws<InvalidDataException>(() => Prepare(target, pack.Input));
        }

        [Fact]
        public void LayeredImages_UseTheLastCompleteMappingWithoutFillingMissingPages()
        {
            using var pack = CreatePack();
            var pages = pack.Discover().Packages[0].Targets[0];
            var image = new ReplacementTarget { Owner = pages.Owner, ImagePath = pages.PagePaths["a.png"] };
            PreparedSpine Compose(params ReplacementTarget[] layers) => ReplacementPreparation.Spine(
                EncryptedPackFixture.Skeleton, EncryptedPackFixture.Atlas, 1f / 64f, layers, pack.Input,
                Path.Combine(pack.Input, "Sensitive"), true, default, true);
            Assert.Equal(2, Compose(image, pages).PageBytes.Count);
            Assert.Throws<InvalidDataException>(() => Compose(pages, image));
            var incomplete = new ReplacementTarget { Owner = pages.Owner };
            incomplete.Pages.Add(new ResourcePageDraft("a.png", "a.png"));
            incomplete.PagePaths.Add("a.png", pages.PagePaths["a.png"]);
            Assert.Throws<InvalidDataException>(() => Compose(pages, incomplete));
        }

        [Fact]
        public void MultiPageDependencies_RecheckFilesAndSensitiveBoundary()
        {
            using var pack = CreatePack();
            var target = pack.Discover().Packages[0].Targets[0];
            string outside = pack.Write("Sensitive/b.png", EncryptedPackFixture.Png);
            target.PagePaths["b.png"] = outside;
            Assert.Throws<InvalidDataException>(() => Prepare(target, pack.Input));
            target.PagePaths["b.png"] = Path.Combine(pack.Input, "missing.png");
            Assert.Throws<FileNotFoundException>(() => Prepare(target, pack.Input));
        }

        [Fact]
        public void Playback_ReusesTracksQueuesMixingAndEventSubscriptions()
        {
            var original = AnimationData(); var replacement = AnimationData();
            var state = new AnimationState(new AnimationStateData(original) { DefaultMix = 1f });
            var skeleton = new Skeleton(original);
            var from = state.SetAnimation(0, "walk", true);
            state.Update(0.5f); state.Apply(skeleton);
            var current = state.SetAnimation(0, "run", true);
            var queued = state.AddAnimation(0, "walk", true, 3f);
            var upper = state.SetAnimation(1, "walk", true);
            current.Alpha = 0.7f;
            int stateEvents = 0, entryEvents = 0;
            state.Event += (_, _) => stateEvents++;
            current.Event += (_, _) => entryEvents++;
            state.Update(0.1f); state.Apply(skeleton);
            float time = current.TrackTime, mix = current.MixTime;
            SpinePlayback.Rebind(state, new AnimationStateData(replacement) { DefaultMix = 1f });
            Assert.Same(current, state.GetCurrent(0));
            Assert.Same(upper, state.GetCurrent(1));
            Assert.Same(from, current.MixingFrom);
            Assert.Same(queued, current.Next);
            Assert.Same(replacement.FindAnimation("run"), current.Animation);
            Assert.Same(replacement.FindAnimation("walk"), queued.Animation);
            Assert.Equal(time, current.TrackTime); Assert.Equal(mix, current.MixTime); Assert.Equal(0.7f, current.Alpha);
            skeleton = SpinePlayback.CreateSkeleton(skeleton, replacement);
            state.Update(0f); state.Apply(skeleton);
            Assert.Equal(0, stateEvents);
            state.Update(0.4f); state.Apply(skeleton);
            Assert.Equal(1, stateEvents); Assert.Equal(1, entryEvents);
        }

        [Fact]
        public void Playback_RejectsMissingAnimationBeforeChangingLiveTracksAndPreservesSkins()
        {
            var original = AnimationData(); var replacement = AnimationData();
            var state = new AnimationState(new AnimationStateData(original));
            var track = state.SetAnimation(0, "run", true);
            replacement.Animations.Remove(replacement.FindAnimation("run"));
            Assert.Throws<InvalidDataException>(() => SpinePlayback.Rebind(state, new AnimationStateData(replacement)));
            Assert.Same(original.FindAnimation("run"), track.Animation);
            Assert.Same(original, state.Data.SkeletonData);
            original.Skins.Add(new Skin("extra")); replacement.Skins.Add(new Skin("extra"));
            var skeleton = new Skeleton(original) { ScaleX = -1, ScaleY = 2, A = 0.5f };
            skeleton.SetSkin("default"); skeleton.MergeSkin(original.FindSkin("extra"));
            var next = SpinePlayback.CreateSkeleton(skeleton, replacement);
            Assert.Equal(new[] { "default", "extra" }, ReplacementRuntime.CaptureSkinNames(next.SkinList));
            Assert.Equal(-1, next.ScaleX); Assert.Equal(2, next.ScaleY); Assert.Equal(0.5f, next.A);
        }

        private static SkeletonData AnimationData()
        {
            string json = EncryptedPackFixture.Skeleton.Replace("\"animations\":{\"stand\":{}}", """
                "events":{"hit":{}},"animations":{
                "walk":{"bones":{"root":{"rotate":[{}, {"time":2,"value":10}]}}},
                "run":{"bones":{"root":{"rotate":[{}, {"time":2,"value":20}]}},"events":[{"time":0.4,"name":"hit"}]}}
                """);
            return new SkeletonJson(PortraitCatalog.ReadAtlas(EncryptedPackFixture.Atlas)).ReadSkeletonData(new StringReader(json));
        }

        private static EncryptedPackFixture CreatePack()
        {
            var pack = new EncryptedPackFixture();
            pack.WriteText("pack.replacement.json", Manifest("fatal_nusi_0", "fatal_nusi_1"));
            pack.WriteText("two.atlas", Atlas);
            pack.Write("a.png", EncryptedPackFixture.Png); pack.Write("b.png", EncryptedPackFixture.Png);
            return pack;
        }

        private static PreparedSpine Prepare(ReplacementTarget target, string root) => ReplacementPreparation.Spine(
            EncryptedPackFixture.Skeleton, EncryptedPackFixture.Atlas, 1f / 64f, new[] { target }, root,
            Path.Combine(root, "Sensitive"), true, default, true);
    }
}
