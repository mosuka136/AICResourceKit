using AICResourceKit.Patches.ReplaceTexture;
using System.Reflection;
using System.Text;

namespace AICResourceKit.Test.Patches.ReplaceTexture
{
    public sealed class ReplacementDiagnosticsTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "be-diagnostics-" + Guid.NewGuid().ToString("N"));

        public ReplacementDiagnosticsTests() => Directory.CreateDirectory(directory);

        [Fact]
        public void Runtime_StartsDisabledAndIgnoresObservations()
        {
            Assert.False(ReplacementDiagnosticRuntime.Enabled);
            ReplacementDiagnosticRuntime.Mti("PxlNoel/noel.pxls.bytes.texture_0", null, "entry-hit", "container-returned");
            ReplacementDiagnosticRuntime.Flush();
            Assert.False(ReplacementDiagnosticRuntime.Enabled);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ;  ; ")]
        public void EmptyFilter_RecordsAllTargets(string filter)
        {
            var session = new ReplacementDiagnosticSession(filter);
            Assert.True(session.Matches(ReplacementDiagnosticTarget.Mti("A", null)));
            Assert.True(session.Matches(ReplacementDiagnosticTarget.Resources("UI/Icon", "Sprite")));
            Assert.False(session.Matches(null));
        }

        [Fact]
        public void Filter_UsesCaseInsensitiveAlternativesWithoutChangingIdentities()
        {
            var session = new ReplacementDiagnosticSession(" stand_battle ; TUTO_MP4/ ");
            Assert.True(session.Matches(ReplacementDiagnosticTarget.Spine("stand_battle", "stand_battle.old")));
            Assert.True(session.Matches(new ReplacementDiagnosticTarget("video-clip", "Tuto_mp4/test", "test", "VideoClip")));
            Assert.False(session.Matches(ReplacementDiagnosticTarget.Spine("stand_normal", "stand_normal")));
            Assert.NotEqual(ReplacementDiagnosticTarget.Mti("A", "image").RuntimeIdentity,
                ReplacementDiagnosticTarget.Mti("a", "image").RuntimeIdentity);
        }

        [Fact]
        public void Addresses_PreserveContainersVariantsAndEmptyMtiImageKey()
        {
            var old = ReplacementDiagnosticTarget.Spine("stand_battle", "stand_battle.old");
            Assert.Equal("spine\nstand_battle\nstand_battle.old", old.RuntimeIdentity);
            Assert.Null(ReplacementDiagnosticTarget.Spine("stand_battle", null).RuntimeIdentity);
            Assert.Equal("texture\nmti\nPxlNoel/noel.pxls.bytes.texture_0\n",
                ReplacementDiagnosticTarget.Mti("PxlNoel/noel.pxls.bytes.texture_0", null).RuntimeIdentity);
            Assert.Null(ReplacementDiagnosticTarget.Mti("A", null).Key);
            Assert.NotEqual(ReplacementDiagnosticTarget.Mti("A", "image").RuntimeIdentity,
                ReplacementDiagnosticTarget.Mti("B", "image").RuntimeIdentity);
            var direct = new ReplacementDiagnosticTarget("mti-image", "A", "image", "Texture");
            Assert.Null(direct.RuntimeIdentity);
        }

        [Theory]
        [InlineData("Assets/Editor/AssetBundlesSrc/MTI_title/", "MTI_title")]
        [InlineData("Assets/Editor/AssetBundlesSrc/SpineAnim/stand_battle.atlas/", "SpineAnim/stand_battle.atlas")]
        [InlineData("Assets/Editor/AssetBundlesSrc/A/../B/", "A/../B")]
        [InlineData("C:/Game/StreamingAssets/title.dat", null)]
        [InlineData("Assets/Editor/AssetBundlesSrc/MTI_title", null)]
        [InlineData(null, null)]
        [InlineData("Assets/Editor/AssetBundlesSrc/", null)]
        public void MtiKey_ReadsExactConstructorScopeWithoutFilenameGuessing(string source, string expected)
        {
            Assert.Equal(expected, ReplacementDiagnosticRuntime.MtiKey(source));
        }

        [Fact]
        public void Recording_DoesNotPromoteDiscoveryOrEntryHitToApplication()
        {
            var session = new ReplacementDiagnosticSession("");
            var target = ReplacementDiagnosticTarget.Spine("stand_battle", "stand_battle.old");
            session.Record(target, "discovered", "catalog", "manifest-target");
            session.Record(target, "entry-hit", "prepare", "prepare-requested");
            session.Record(target, "entry-hit", "prepare", "prepare-requested");
            var observations = Observations(session);
            Assert.Equal(2, observations.Count);
            Assert.DoesNotContain(observations, row => (string)row["stage"] == "candidate-applied");
            Assert.Equal(2, PortraitJson.Integer(observations.Single(row => (string)row["stage"] == "entry-hit")["count"]));
            Assert.Equal("not-performed", Snapshot(session)["visualVerification"]);
        }

        [Fact]
        public void FailureAfterApplication_PreservesBothEvidenceAndFailureReason()
        {
            var session = new ReplacementDiagnosticSession("UI/Icon");
            var target = ReplacementDiagnosticTarget.Resources("UI/Icon", "Sprite");
            session.Record(target, "candidate-applied", "load", "object-ready");
            session.Record(target, "candidate-failed", "load", "InvalidDataException", "dimensions differ");
            session.Record(target, "released", "dispose", "replacement-disposed");
            var observations = Observations(session);
            Assert.Equal(3, observations.Count);
            Assert.Equal("dimensions differ", observations.Single(row => (string)row["stage"] == "candidate-failed")["reason"]);
            Assert.Contains(observations, row => (string)row["stage"] == "candidate-applied");
        }

        [Fact]
        public void ObservationKeys_DoNotCollideForNullEmptyOrDelimiters()
        {
            var session = new ReplacementDiagnosticSession("");
            foreach (var target in new[]
            {
                new ReplacementDiagnosticTarget("mti-image", "a\nb", "c", "Texture"),
                new ReplacementDiagnosticTarget("mti-image", "a", "b\nc", "Texture"),
                ReplacementDiagnosticTarget.Mti("a", null), ReplacementDiagnosticTarget.Mti("a", "")
            }) session.Record(target, "entry-hit", "load", "returned");
            Assert.Equal(4, Observations(session).Count);
        }

        [Fact]
        public void Capacity_ReportsTruncationAndStillCountsExistingEntries()
        {
            var session = new ReplacementDiagnosticSession("", 1);
            var first = ReplacementDiagnosticTarget.Mti("A", null);
            session.Record(first, "entry-hit", "load", "returned");
            session.Record(ReplacementDiagnosticTarget.Mti("B", null), "entry-hit", "load", "returned");
            session.Record(first, "entry-hit", "load", "returned");
            Assert.Equal(1, PortraitJson.Integer(Snapshot(session)["droppedObservations"]));
            Assert.Equal(2, PortraitJson.Integer(Assert.Single(Observations(session))["count"]));
        }

        [Fact]
        public void FilteredTargetsAndNullTargets_DoNotConsumeCapacity()
        {
            var session = new ReplacementDiagnosticSession("wanted", 1);
            session.Record(null, "entry-hit", "load", "none");
            session.Record(ReplacementDiagnosticTarget.Mti("other", null), "entry-hit", "load", "returned");
            session.Record(ReplacementDiagnosticTarget.Mti("wanted", null), "entry-hit", "load", "returned");
            Assert.Single(Observations(session));
            Assert.Equal(0, PortraitJson.Integer(Snapshot(session)["droppedObservations"]));
        }

        [Fact]
        public void Export_WritesCompleteUtf8ReportAndReplacesPreviousReport()
        {
            string path = Path.Combine(directory, "report.json");
            var session = new ReplacementDiagnosticSession("图集");
            session.Record(ReplacementDiagnosticTarget.Mti("图集", "I"), "entry-hit", "load", "returned");
            session.Export(path, "test", new Dictionary<string, object>(), new List<object>());
            Assert.False(session.Dirty);
            Assert.EndsWith("\r\n", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".tmp"));
            Assert.False(File.ReadAllBytes(path).Take(3).SequenceEqual(Encoding.UTF8.GetPreamble()));
            session.Record(ReplacementDiagnosticTarget.Mti("图集", "I"), "candidate-failed", "load", "bad", "缺页");
            Assert.True(session.Dirty);
            session.Export(path, "test", new Dictionary<string, object>(), new List<object>());
            Assert.Equal(2, PortraitJson.Array(PortraitJson.Parse(File.ReadAllText(path))["observations"]).Count);
        }

        [Fact]
        public void FailedExport_KeepsObservationsForRetryAndRemovesTemporaryFile()
        {
            string path = Path.Combine(directory, "occupied");
            Directory.CreateDirectory(path);
            var session = new ReplacementDiagnosticSession("");
            session.Record(ReplacementDiagnosticTarget.Mti("A", null), "entry-hit", "load", "returned");
            Assert.ThrowsAny<IOException>(() => session.Export(path, "test", new Dictionary<string, object>(), new List<object>()));
            Assert.True(session.Dirty);
            Assert.Single(Observations(session));
            Assert.False(File.Exists(path + ".tmp"));
        }

        [Fact]
        public void HookDefinitions_ResolveExactBaselineMethodsAndCompatibleCallbacks()
        {
            var hooks = ReplacementDiagnosticHooks.Definitions().ToArray();
            Assert.Equal(16, hooks.Length);
            foreach (var hook in hooks)
            {
                var method = hook.Resolve();
                Assert.NotNull(method);
                Assert.Equal(hook.Parameters, method.GetParameters().Select(parameter => parameter.ParameterType));
                var callback = typeof(ReplacementDiagnosticHooks).GetMethod(hook.Callback, BindingFlags.Static | BindingFlags.NonPublic);
                Assert.NotNull(callback);
                foreach (var parameter in callback.GetParameters())
                {
                    if (parameter.Name == "__instance") Assert.True(parameter.ParameterType.IsAssignableFrom(method.DeclaringType));
                    else if (parameter.Name == "__originalMethod") Assert.Equal(typeof(MethodBase), parameter.ParameterType);
                    else if (parameter.Name == "__result") Assert.Equal(method.ReturnType, parameter.ParameterType);
                    else
                    {
                        var original = Assert.Single(method.GetParameters(), item => item.Name == parameter.Name);
                        Type originalType = original.ParameterType.IsByRef ? original.ParameterType.GetElementType() : original.ParameterType;
                        Assert.Equal(originalType, parameter.ParameterType);
                    }
                }
            }
            Assert.Equal(8, ReplacementDiagnosticHooks.ExistingEntries().Count());
            Assert.All(ReplacementDiagnosticHooks.ExistingEntries(), Assert.NotNull);
        }

        [Fact]
        public void InvalidStage_IsRejectedInsteadOfCreatingSuccessEvidence()
        {
            var session = new ReplacementDiagnosticSession("");
            Assert.Throws<ArgumentException>(() => session.Record(ReplacementDiagnosticTarget.Mti("A", null), "success", "load", "ok"));
            Assert.Empty(Observations(session));
        }

        private static Dictionary<string, object> Snapshot(ReplacementDiagnosticSession session) =>
            PortraitJson.Parse(session.Serialize("test", new Dictionary<string, object>(), new List<object>()));

        private static List<Dictionary<string, object>> Observations(ReplacementDiagnosticSession session) =>
            PortraitJson.Array(Snapshot(session)["observations"]).Select(PortraitJson.Object).ToList();

        public void Dispose() => Directory.Delete(directory, true);
    }
}
