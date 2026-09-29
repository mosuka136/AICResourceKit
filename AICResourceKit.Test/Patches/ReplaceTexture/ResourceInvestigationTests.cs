using System.Text.Json;

namespace AICResourceKit.Test.Patches.ReplaceTexture
{
    public sealed class ResourceInvestigationTests : IDisposable
    {
        private readonly JsonDocument report = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "ver030g-investigation.json")));

        [Fact]
        public void Inventory_CoversEveryConfirmedAndPendingGroupWithoutPromotingEvidence()
        {
            var root = report.RootElement;
            var groups = root.GetProperty("groups").EnumerateArray().ToArray();
            Assert.Equal(150, groups.Length);
            Assert.Equal(150, groups.Select(group => group.GetProperty("sourceGroupId").GetString()).Distinct().Count());
            Assert.Equal(141, groups.Count(group => group.GetProperty("membership").GetString() == "confirmed"));
            Assert.Equal(9, groups.Count(group => group.GetProperty("membership").GetString() == "pending"));
            var routes = root.GetProperty("routes").EnumerateArray().Select(route => route.GetProperty("id").GetString()).ToHashSet();
            foreach (var group in groups)
            {
                Assert.Equal("not-observed", group.GetProperty("runtimeHit").GetString());
                Assert.Equal("not-observed", group.GetProperty("replacementApplied").GetString());
                Assert.Equal("not-performed", group.GetProperty("visualVerification").GetString());
                Assert.NotEmpty(group.GetProperty("pendingReasons").EnumerateArray());
                Assert.NotEmpty(group.GetProperty("routes").EnumerateArray());
                Assert.All(group.GetProperty("routes").EnumerateArray(), route => Assert.Contains(route.GetString(), routes));
                Assert.NotEmpty(group.GetProperty("sources").EnumerateArray());
                foreach (var source in group.GetProperty("sources").EnumerateArray())
                {
                    Assert.False(Path.IsPathRooted(source.GetProperty("container").GetString()));
                    Assert.Contains(source.GetProperty("pathId").ValueKind, new[] { JsonValueKind.String, JsonValueKind.Null });
                }
            }
        }

        [Fact]
        public void Inventory_PreservesEquipmentOnlyAndPendingMembership()
        {
            var groups = report.RootElement.GetProperty("groups").EnumerateArray().ToArray();
            Assert.Equal(new[] { "cane_bermit.pxls", "sgwall.pxls", "syabon.pxls" }, groups
                .Where(group => group.GetProperty("coverage").GetString() == "equipment-only")
                .Select(group => group.GetProperty("name").GetString()).OrderBy(name => name));
            Assert.Equal(5, groups.Count(group => group.GetProperty("kind").GetString() == "mobpcc"
                && group.GetProperty("membership").GetString() == "pending"));
            Assert.Equal(2, report.RootElement.GetProperty("excludedVideoChecks").GetArrayLength());
        }

        [Fact]
        public void Inventory_KeepsSharedAtlasHistoricalJsonAndNullImageArguments()
        {
            var battle = Find("stand_battle.old").GetProperty("runtimeArguments")[0];
            Assert.Equal("stand_battle", battle.GetProperty("key").GetString());
            Assert.Equal("stand_battle.old", battle.GetProperty("jsonKey").GetString());
            var noel = Find("noel.pxls").GetProperty("runtimeArguments")[0];
            Assert.Equal(JsonValueKind.Null, noel.GetProperty("imageKey").ValueKind);
            Assert.Equal("texture\nmti\nPxlNoel/noel.pxls.bytes.texture_0\n", noel.GetProperty("runtimeIdentity").GetString());
            var shared = Find("fatal_nusi_1").GetProperty("runtimeArguments")[0];
            Assert.Equal("Fatal/fatal_nusi_0.atlas", shared.GetProperty("container").GetString());
            Assert.Equal("fatal_nusi_1", shared.GetProperty("jsonKey").GetString());
            Assert.Equal(JsonValueKind.Null, shared.GetProperty("runtimeIdentity").ValueKind);
            Assert.Empty(Find("wplmode_").GetProperty("runtimeArguments").EnumerateArray());
            Assert.Empty(Find("damage_backvoreenemy.atlas").GetProperty("runtimeArguments").EnumerateArray());
        }

        private JsonElement Find(string name) => report.RootElement.GetProperty("groups").EnumerateArray()
            .Single(group => group.GetProperty("name").GetString() == name);

        public void Dispose() => report.Dispose();
    }
}
