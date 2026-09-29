using AICResourceKit.Contracts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AICResourceKit.Patches.ReplaceTexture
{
    // Diagnostic addresses describe observed arguments, not a new replacement manifest format.
    internal sealed class ReplacementDiagnosticTarget
    {
        internal readonly string Loader;
        internal readonly string Container;
        internal readonly string Key;
        internal readonly string ObjectType;
        internal readonly string RuntimeIdentity;

        internal ReplacementDiagnosticTarget(string loader, string container, string key,
            string objectType, string runtimeIdentity = null)
        {
            Loader = loader ?? throw new ArgumentNullException(nameof(loader));
            Container = container;
            Key = key;
            ObjectType = objectType;
            RuntimeIdentity = runtimeIdentity;
        }

        internal Dictionary<string, object> Describe() => new Dictionary<string, object>
        {
            ["loader"] = Loader, ["container"] = Container, ["key"] = Key,
            ["objectType"] = ObjectType, ["runtimeIdentity"] = RuntimeIdentity
        };

        internal static ReplacementDiagnosticTarget Spine(string key, string jsonKey) =>
            new ReplacementDiagnosticTarget("portrait-spine", key, jsonKey, "SkeletonDataAsset",
                key == null || jsonKey == null ? null : ResourceIdentity.Spine(key, jsonKey));

        internal static ReplacementDiagnosticTarget Mti(string assetKey, string imageKey) =>
            new ReplacementDiagnosticTarget("mti-one-image", assetKey, imageKey, "Texture",
                ResourceIdentity.Mti(assetKey, imageKey));

        internal static ReplacementDiagnosticTarget Resources(string path, string objectType) =>
            new ReplacementDiagnosticTarget("resources", null, path, objectType,
                objectType == "Sprite" || objectType == "Texture2D"
                    ? ResourceIdentity.Resources(path, objectType) : null);

        internal static ReplacementDiagnosticTarget Pxl(PxlResourceAddress address) =>
            new ReplacementDiagnosticTarget("pxl", address.AssetKey, address.TextKey, "Texture", address.Identity);

        internal static ReplacementDiagnosticTarget SpineAssets(SpineResourceAddress address) =>
            new ReplacementDiagnosticTarget("spine-assets", address.AssetKey, address.JsonKey, "SkeletonDataAsset", address.Identity);

        internal static ReplacementDiagnosticTarget FromManifest(ReplacementTarget target) =>
            target.Type == "spine-assets" ? SpineAssets(target.SpineAddress)
                : target.Type == "spine" ? Spine(target.SpineKey, target.JsonKey)
                : target.Loader == "pxl" ? Pxl(target.PxlAddress)
                : target.Loader == "mti" ? Mti(target.AssetKey, target.ImageKey)
                : Resources(target.ResourcePath, target.ObjectType);
    }

    internal sealed class ReplacementDiagnosticSession
    {
        private readonly Dictionary<string, Dictionary<string, object>> observations =
            new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        private readonly string[] filters;
        private readonly int capacity;
        private readonly string sessionId = Guid.NewGuid().ToString("N");
        private readonly string startedUtc = UtcNow();
        private long dropped;

        internal ReplacementDiagnosticSession(string filter, int capacity = 4096)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            Filter = filter ?? "";
            filters = Filter.Split(';').Select(value => value.Trim()).Where(value => value.Length > 0).ToArray();
            this.capacity = capacity;
        }

        internal string Filter { get; }
        internal bool Dirty { get; private set; } = true;

        internal bool Matches(ReplacementDiagnosticTarget target)
        {
            if (target == null) return false;
            if (filters.Length == 0) return true;
            var fields = new[] { target.Loader, target.Container, target.Key, target.ObjectType, target.RuntimeIdentity };
            return filters.Any(filter => fields.Any(value => value != null
                && value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        internal void Record(ReplacementDiagnosticTarget target, string stage, string entry,
            string outcome, string reason = null, Dictionary<string, object> details = null)
        {
            if (!Matches(target)) return;
            if (!new[] { "discovered", "entry-hit", "candidate-pending", "candidate-applied",
                "candidate-failed", "restored", "released" }.Contains(stage))
                throw new ArgumentException("Unknown diagnostic stage.", nameof(stage));
            // JSON encoding keeps null, empty and delimiter-containing arguments distinct.
            string id = PortraitJson.Serialize(new object[] { target.Describe(), stage, entry, outcome });
            Dirty = true;
            if (!observations.TryGetValue(id, out var record))
            {
                if (observations.Count >= capacity) { dropped++; return; }
                record = new Dictionary<string, object>
                {
                    ["target"] = target.Describe(), ["stage"] = stage, ["entry"] = entry,
                    ["outcome"] = outcome, ["firstUtc"] = UtcNow(), ["count"] = 0L
                };
                observations.Add(id, record);
            }
            record["count"] = (long)record["count"] + 1;
            record["lastUtc"] = UtcNow();
            record["reason"] = reason;
            record["details"] = details;
        }

        internal string Serialize(string pluginVersion, Dictionary<string, object> assemblies,
            IList<object> hooks)
        {
            return PortraitJson.Serialize(new Dictionary<string, object>
            {
                ["reportVersion"] = 1, ["evidenceKind"] = "runtime-observation",
                ["pluginVersion"] = pluginVersion, ["assemblies"] = assemblies,
                ["sessionId"] = sessionId, ["startedUtc"] = startedUtc, ["exportedUtc"] = UtcNow(),
                ["filter"] = Filter, ["capacity"] = capacity, ["droppedObservations"] = dropped,
                ["visualVerification"] = "not-performed", ["hooks"] = hooks,
                ["observations"] = observations.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => (object)pair.Value).ToArray()
            });
        }

        internal void Export(string path, string pluginVersion, Dictionary<string, object> assemblies,
            IList<object> hooks)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            // A reader sees either the previous complete report or the next complete report.
            string temporary = path + ".tmp";
            try
            {
                File.WriteAllText(temporary, Serialize(pluginVersion, assemblies, hooks) + "\r\n", new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                Dirty = false;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static string UtcNow() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
    }
}
