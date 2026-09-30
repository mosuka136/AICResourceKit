using AICResourceKit.Contracts;
using AICResourceKit.Patches;
using AICResourceKit.Patches.ReplaceTexture;
using nel;

namespace AICResourceKit.Test.Patches.ReplaceTexture
{
    public sealed class ReplacementPreviewTests
    {
        [Fact]
        public void NewlyEnabledPortraits_IgnoreDisablingReorderingTexturesAndInvalidTargets()
        {
            var catalog = new ReplacementCatalog();
            var stand = Add(catalog, "stand", "stand");
            var bench = Add(catalog, "bench", "bench");
            var texture = Add(catalog, "texture", "texture");
            texture.Type = "texture";
            var bad = Add(catalog, "bad", "bad");
            bad.Owner.InvalidTargetIdentities.Add(bad.Identity);
            var before = Select(catalog, "stand");
            var after = Select(catalog, "stand", "bench", "texture", "bad");
            Assert.Equal(new[] { bench }, after.NewlyEnabledPortraits(before));
            Assert.Empty(before.NewlyEnabledPortraits(after));
            Assert.Empty(Select(catalog, "bench", "stand", "texture", "bad").NewlyEnabledPortraits(after));
            Assert.Equal(new[] { stand, bench }, after.NewlyEnabledPortraits(new ReplacementSelection(catalog, after.EnabledIds, false, true)));
        }

        [Fact]
        public void NewlyEnabledPortraits_RespectSensitiveAuthorization()
        {
            var catalog = new ReplacementCatalog();
            var secret = Add(catalog, "secret", "bench");
            secret.Owner.Sensitive = true;
            var denied = new ReplacementSelection(catalog, new[] { "secret" }, true, false);
            var allowed = Select(catalog, "secret");
            Assert.Empty(denied.NewlyEnabledPortraits(allowed));
            Assert.Equal(new[] { secret }, allowed.NewlyEnabledPortraits(denied));
        }

        [Fact]
        public void PreviewLayers_PromoteSelectedResourceWithoutChangingConfiguredOrder()
        {
            var catalog = new ReplacementCatalog();
            var low = Add(catalog, "low", "stand");
            var middle = Add(catalog, "middle", "stand");
            var high = Add(catalog, "high", "stand");
            var other = Add(catalog, "other", "bench");
            var selection = Select(catalog, "low", "middle", "high", "other");
            Assert.Equal(new[] { middle, high, low }, ReplacementPreview.Layers(selection, low));
            Assert.Equal(new[] { low, high, middle }, ReplacementPreview.Layers(selection, middle));
            Assert.Equal(new[] { low, middle, high }, selection.Layers(low.Identity));
            Assert.Equal(new[] { other }, selection.Layers(other.Identity));
            Assert.Empty(ReplacementPreview.Layers(Select(catalog, "high"), low));
        }

        [Fact]
        public void PreviewComposition_ShowsNewLowPriorityPackThenNormalCompositionStillShowsHighPack()
        {
            using var pack = new EncryptedPackFixture();
            pack.CreatePack(15, "low", "low");
            pack.CreatePack(15, "high", "high");
            pack.WriteText("low/page.json", EncryptedPackFixture.Skeleton.Replace("\"name\":\"root\"", "\"name\":\"root\",\"x\":10"), true);
            pack.WriteText("high/page.json", EncryptedPackFixture.Skeleton.Replace("\"name\":\"root\"", "\"name\":\"root\",\"x\":20"), true);
            var catalog = pack.Discover();
            var selection = Select(catalog, "low", "high");
            var normal = selection.Layers(EncryptedPackFixture.SpineIdentity);
            var target = normal.First();
            var temporary = ReplacementPreview.Layers(selection, target);
            string root = pack.Input, sensitive = Path.Combine(root, "Sensitive");
            var preview = ReplacementPreparation.Spine(EncryptedPackFixture.Skeleton, EncryptedPackFixture.Atlas,
                1, temporary, root, sensitive, false, default);
            var restored = ReplacementPreparation.Spine(EncryptedPackFixture.Skeleton, EncryptedPackFixture.Atlas,
                1, normal, root, sensitive, false, default);
            Assert.Equal(10, preview.Composition.PreparedData.FindBone("root").X);
            Assert.Equal(20, restored.Composition.PreparedData.FindBone("root").X);
            Assert.Equal(new[] { "low", "high" }, selection.EnabledIds);
            Assert.Equal(new[] { "low", "high" }, normal.Select(layer => layer.PackageId));
            Assert.Equal(new[] { "high", "low" }, temporary.Select(layer => layer.PackageId));
        }

        [Fact]
        public void PreviewLayers_RejectRevokedSensitiveAndDamagedTargets()
        {
            var catalog = new ReplacementCatalog();
            var target = Add(catalog, "pack", "stand");
            target.Owner.Sensitive = true;
            Assert.Empty(ReplacementPreview.Layers(new ReplacementSelection(catalog, new[] { "pack" }, true, false), target));
            target.Owner.InvalidTargetIdentities.Add(target.Identity);
            Assert.Empty(ReplacementPreview.Layers(Select(catalog, "pack"), target));
        }

        [Fact]
        public void Choose_PrefersCurrentPoseWithinHighestPriorityNewPackage()
        {
            var catalog = new ReplacementCatalog();
            var low = Add(catalog, "low", "stand");
            var high = Add(catalog, "high", "bench");
            var highStand = new ReplacementTarget { Owner = high.Owner, PackageId = "high", Type = "spine", SpineKey = "stand", JsonKey = "stand" };
            var stand = Pose(UIEMOT.STAND, "stand");
            var bench = Pose(UIEMOT.BENCH, "bench");
            Assert.Same(stand, ReplacementPreview.Choose(new[] { low, highStand, high }, new[] { stand, bench }, out var currentTarget));
            Assert.Same(highStand, currentTarget);
            Assert.Same(bench, ReplacementPreview.Choose(new[] { low, high }, new[] { stand, bench }, out var highestTarget));
            Assert.Same(high, highestTarget);
        }

        [Fact]
        public void Choose_RequiresExactJsonVariantAndExcludesCutinsAndRegularImages()
        {
            var catalog = new ReplacementCatalog();
            var target = Add(catalog, "pack", "shared");
            target.JsonKey = "variant";
            var variant = Pose(UIEMOT.DOWN, "shared");
            variant.JsonKey = "variant";
            variant.Selection = new PortraitSelection(UIEMOT.DOWN, UIPictureBase.EMSTATE.TORNED);
            var cutin = Pose(UIEMOT.CUTS_COW_0, "shared");
            cutin.JsonKey = "variant";
            var wrongJson = Pose(UIEMOT.STAND, "shared");
            Assert.Null(ReplacementPreview.Choose(new[] { target }, new[] { cutin, wrongJson }, out _));
            Assert.Same(variant, ReplacementPreview.Choose(new[] { target }, new[] { cutin, wrongJson, variant }, out _));
            target.Type = "texture";
            Assert.Null(ReplacementPreview.Choose(new[] { target }, new[] { variant }, out _));
            Assert.Null(ReplacementPreview.Choose(Array.Empty<ReplacementTarget>(), new[] { variant }, out _));
        }

        [Fact]
        public void NewlyEnabledPortraits_IncludeConditionalSpineAndPxlOnly()
        {
            var catalog = new ReplacementCatalog();
            var spine = Add(catalog, "spine", "weak");
            var pxl = Add(catalog, "pxl", "unused");
            var shared = Add(catalog, "shared", "unused");
            spine.PortraitSelection = Filter("""{"animations":["weak"]}""");
            pxl.Type = shared.Type = "texture";
            pxl.Loader = shared.Loader = "pxl";
            pxl.PxlAddress = shared.PxlAddress = PxlResourceAddress.Page("Pxl/noel", "noel.pxls", 0);
            pxl.PortraitSelection = Filter("""{"excludeStates":["TORNED"]}""");
            Assert.Equal(new[] { spine, pxl }, Select(catalog, "spine", "pxl", "shared").NewlyEnabledPortraits(Select(catalog)));
        }

        [Fact]
        public void Choose_UsesMatchingBaseAnimationAndNormalVariantInsteadOfCurrentTornState()
        {
            var target = Add(new ReplacementCatalog(), "normal", "weak");
            target.PortraitSelection = Filter("""{"poses":["STAND"],"animations":["weak"],"excludeStates":["TORNED"]}""");
            var torn = Pose(UIEMOT.STAND, "weak");
            torn.Selection = new PortraitSelection(UIEMOT.STAND, UIPictureBase.EMSTATE.LOWHP | UIPictureBase.EMSTATE.TORNED);
            torn.Animations = new[] { "stand", "weak" };
            var normal = Pose(UIEMOT.STAND, "weak");
            normal.Selection = new PortraitSelection(UIEMOT.STAND,
                (UIPictureBase.EMSTATE)target.PortraitSelection.PreviewState((uint)torn.Selection.State));
            normal.Animations = torn.Animations;
            Assert.Same(normal, ReplacementPreview.Choose(new[] { target }, new[] { torn, normal }, out var chosen));
            Assert.Same(target, chosen);
            Assert.Equal("weak", normal.Animation);
            Assert.Equal(UIPictureBase.EMSTATE.LOWHP, normal.Selection.State);
            normal.Animations = new[] { "stand" };
            Assert.Null(ReplacementPreview.Choose(new[] { target }, new[] { torn, normal }, out _));
        }

        [Fact]
        public void PreviewLayers_FilterConditionsBeforeTemporarilyPromotingTarget()
        {
            var catalog = new ReplacementCatalog();
            var target = Add(catalog, "normal", "weak");
            var torn = Add(catalog, "torn", "weak");
            var whole = Add(catalog, "whole", "weak");
            target.PortraitSelection = Filter("""{"animations":["weak"],"excludeStates":["TORNED"]}""");
            torn.PortraitSelection = Filter("""{"requireStates":["TORNED"]}""");
            var pose = Pose(UIEMOT.STAND, "weak");
            pose.Animation = "weak";
            var selection = Select(catalog, "normal", "torn", "whole");
            Assert.Equal(new[] { whole, target }, ReplacementPreview.Layers(selection, target, pose));
            Assert.Equal(new[] { target, torn, whole }, selection.Layers(target.Identity));
            pose.Selection = new PortraitSelection(UIEMOT.STAND, UIPictureBase.EMSTATE.TORNED);
            Assert.Empty(ReplacementPreview.Layers(selection, target, pose));
        }

        [Fact]
        public void Choose_PxlRequiresObservedPageIdentityAndMatchingPoseAndState()
        {
            var target = Add(new ReplacementCatalog(), "pxl", "unused");
            target.Type = "texture";
            target.Loader = "pxl";
            target.PxlAddress = PxlResourceAddress.Page("Pxl/noel", "noel.pxls", 0);
            target.PortraitSelection = Filter("""{"poses":["DAMAGE_0"],"excludeStates":["TORNED"]}""");
            var pose = Pose(UIEMOT.DAMAGE_0, null);
            Assert.Null(ReplacementPreview.Choose(new[] { target }, new[] { pose }, out _));
            pose.PxlIdentities.Add(target.Identity);
            Assert.Same(pose, ReplacementPreview.Choose(new[] { target }, new[] { pose }, out _));
            pose.Selection = new PortraitSelection(UIEMOT.STAND);
            Assert.Null(ReplacementPreview.Choose(new[] { target }, new[] { pose }, out _));
            pose.Selection = new PortraitSelection(UIEMOT.DAMAGE_0, UIPictureBase.EMSTATE.TORNED);
            Assert.Null(ReplacementPreview.Choose(new[] { target }, new[] { pose }, out _));
        }

        [Theory]
        [InlineData("{\"requireStates\":[\"NORMAL\"]}", 1048608u, 0u)]
        [InlineData("{\"requireStates\":[\"LOWHP\",\"LOWMP\"],\"excludeStates\":[\"TORNED\"]}", 1048576u, 8224u)]
        public void PreviewState_BuildsCandidateWithoutChangingUnrelatedFlags(string json, uint current, uint expected)
        {
            var filter = Filter(json);
            Assert.Equal(expected, filter.PreviewState(current));
            Assert.True(filter.Matches("STAND", expected));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Choose_SkipsPackAlreadyMatchingCurrentPoseStateAndActualAnimation(bool conditional)
        {
            var catalog = new ReplacementCatalog();
            var target = Add(catalog, "pack", "weak");
            if (conditional) target.PortraitSelection = Filter("""{"animations":["weak"],"excludeStates":["TORNED"]}""");
            var current = Pose(UIEMOT.STAND, "weak");
            current.Animation = "weak";
            current.Selection = new PortraitSelection(UIEMOT.STAND, UIPictureBase.EMSTATE.LOWHP);
            Assert.True(ReplacementPreview.MatchesCurrent(target, current));
            Assert.Null(ReplacementPreview.Choose(new[] { target }, new[] { current }, out var chosen, current));
            Assert.Null(chosen);
        }

        [Fact]
        public void Choose_SameBodyWithDifferentActualAnimationStillNeedsPreview()
        {
            var target = Add(new ReplacementCatalog(), "pack", "stand");
            target.PortraitSelection = Filter("""{"animations":["weak"],"excludeStates":["TORNED"]}""");
            var current = Pose(UIEMOT.STAND, "stand");
            current.Animation = "stand";
            current.Animations = new[] { "stand", "weak" };
            var preview = Pose(UIEMOT.STAND, "stand");
            preview.Animations = current.Animations;
            Assert.False(ReplacementPreview.MatchesCurrent(target, current));
            Assert.Same(preview, ReplacementPreview.Choose(new[] { target }, new[] { preview }, out _, current));
            Assert.Equal("weak", preview.Animation);
            Assert.Equal("stand", current.Animation);
            current.Animation = "weak";
            current.Selection = new PortraitSelection(UIEMOT.STAND, UIPictureBase.EMSTATE.TORNED);
            Assert.False(ReplacementPreview.MatchesCurrent(target, current));
        }

        [Fact]
        public void Choose_PxlAlreadyMatchingCurrentPageAndStateDoesNotPreview()
        {
            var target = Add(new ReplacementCatalog(), "pack", "unused");
            target.Type = "texture";
            target.Loader = "pxl";
            target.PxlAddress = PxlResourceAddress.Page("Pxl/noel", "noel.pxls", 0);
            target.PortraitSelection = Filter("""{"poses":["DAMAGE_0"],"excludeStates":["TORNED"]}""");
            var current = Pose(UIEMOT.DAMAGE_0, null);
            current.PxlIdentities.Add(target.Identity);
            Assert.Null(ReplacementPreview.Choose(new[] { target }, new[] { current }, out _, current));
            current.PxlIdentities.Clear();
            Assert.False(ReplacementPreview.MatchesCurrent(target, current));
        }

        [Fact]
        public void Choose_MultiTargetPackDoesNotJumpElsewhereWhenOneTargetAlreadyMatches()
        {
            var catalog = new ReplacementCatalog();
            var stand = Add(catalog, "pack", "stand");
            var bench = new ReplacementTarget { Owner = stand.Owner, PackageId = "pack", Type = "spine", SpineKey = "bench", JsonKey = "bench" };
            var current = Pose(UIEMOT.STAND, "stand");
            Assert.Null(ReplacementPreview.Choose(new[] { bench, stand }, new[] { Pose(UIEMOT.BENCH, "bench"), current }, out _, current));
        }

        private static PortraitResourceSelection Filter(string json) => PortraitResourceSelection.Parse(PortraitJson.Parse(json), true);

        private static ReplacementPreviewPose Pose(UIEMOT pose, string key) => new ReplacementPreviewPose
        { Selection = new PortraitSelection(pose), SpineKey = key, JsonKey = key };

        private static ReplacementSelection Select(ReplacementCatalog catalog, params string[] ids) =>
            new ReplacementSelection(catalog, ids, true, true);

        private static ReplacementTarget Add(ReplacementCatalog catalog, string id, string key)
        {
            var package = new ReplacementPackage { Id = id };
            var target = new ReplacementTarget { Owner = package, PackageId = id, Type = "spine", SpineKey = key, JsonKey = key };
            package.Targets.Add(target);
            catalog.Packages.Add(package);
            return target;
        }
    }
}
