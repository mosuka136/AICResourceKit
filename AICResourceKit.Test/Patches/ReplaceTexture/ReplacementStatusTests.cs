using AICResourceKit.Patches.ReplaceTexture;

namespace AICResourceKit.Test.Patches.ReplaceTexture
{
    public sealed class ReplacementStatusTests
    {
        [Fact]
        public void Snapshot_DistinguishesLoadingPartialApplicationAndRetainedFailure()
        {
            var status = new ReplacementLoadStatus();
            Assert.Equal("not-loaded", status.State(true, true));
            status.Observe(false, false);
            Assert.Equal("loaded", status.State(true, true));
            status.Observe(true, false, new[] { "normal", "override" });
            Assert.Equal("partial", status.State(true, true));
            status.Observe(false, true);
            Assert.Equal("preparing", status.State(true, true));
            status.Errors.Add("Invalid PNG dimensions.");
            Assert.Equal("failed", status.State(true, true));
            Assert.Equal(1, status.Applied);
            Assert.Equal(2, status.AppliedPackages.Count);
            Assert.Equal("unauthorized", status.State(true, false));
            Assert.Equal("disabled", status.State(false, false));
            var complete = new ReplacementLoadStatus();
            complete.Observe(true, false, new[] { "normal" });
            Assert.Equal("applied", complete.State(true, true));
        }

        [Fact]
        public void CatalogIssues_IdentifyUnsupportedTargetsAndMissingFilesWithoutDiscardingValidTargets()
        {
            using var pack = new EncryptedPackFixture();
            pack.Write("page.png", EncryptedPackFixture.Png);
            string manifest = pack.WriteText("pack.replacement.json", """
                {"formatVersion":2,"id":"mixed","targets":[
                {"type":"video","address":{}},
                {"type":"texture","loader":"resources","path":"UI/Missing","objectType":"Texture2D","image":"missing.png"},
                {"type":"texture","loader":"resources","path":"UI/Good","objectType":"Texture2D","image":"page.png"}]}
                """);
            var catalog = pack.Discover();
            var valid = Assert.Single(Assert.Single(catalog.Packages).Targets);
            Assert.Equal(2, valid.TargetIndex);
            Assert.False(valid.Owner.HasUnidentifiedErrorFor(valid.Identity));
            Assert.True(valid.Owner.HasUnidentifiedErrorFor("previously-loaded-but-unidentified"));
            Assert.Equal(2, catalog.Issues.Count);
            var unsupported = catalog.Issues[0];
            Assert.Equal("unsupported-target", unsupported.Code);
            Assert.Equal("mixed", unsupported.PackageId);
            Assert.Equal(0, unsupported.TargetIndex);
            Assert.Equal(manifest, unsupported.Manifest);
            var missing = catalog.Issues[1];
            Assert.Equal("missing-dependency", missing.Code);
            Assert.Equal(1, missing.TargetIndex);
            Assert.EndsWith("missing.png", missing.File);
            Assert.Contains("UI/Missing", missing.Identity);
        }

        [Fact]
        public void CatalogIssues_PreserveUnsupportedVersionAndUnauthorizedManifestIdentity()
        {
            using var pack = new EncryptedPackFixture();
            pack.WriteText("new.replacement.json", """{"formatVersion":99,"id":"future","targets":[]}""");
            pack.CreatePack(folder: "Sensitive", id: "secret");
            var catalog = pack.Discover(false);
            Assert.Empty(catalog.Packages);
            Assert.Equal("unsupported-version", Assert.Single(catalog.Issues).Code);
            Assert.Equal(2, catalog.Manifests.Count);
            Assert.True(catalog.Manifests.Single(info => info.Id == "secret").Sensitive);
            Assert.Contains("secret", catalog.DeclaredIds);
        }

        [Fact]
        public void AuthorizationRevocation_BypassesCoalescingButEnableAndReorderDoNot()
        {
            var selection = new ReplacementSelection(new ReplacementCatalog(), new[] { "normal", "secret" }, true, true);
            Assert.True(selection.RevokedBy(new[] { "normal" }, true, true));
            Assert.True(selection.RevokedBy(new[] { "normal", "secret" }, true, false));
            Assert.True(selection.RevokedBy(new[] { "normal", "secret" }, false, true));
            Assert.False(selection.RevokedBy(new[] { "secret", "normal" }, true, true));
            Assert.False(selection.RevokedBy(new[] { "normal", "secret", "new" }, true, true));
            Assert.True(selection.RevokedBy(new[] { "Normal", "secret" }, true, true));
        }
    }
}
