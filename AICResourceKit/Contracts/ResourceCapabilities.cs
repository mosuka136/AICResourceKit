using System.Collections.Generic;

namespace AICResourceKit.Contracts
{
    /// <summary>编译版本声明的能力；不读取游戏状态，也不代替运行时验证。</summary>
    public static class ResourceCapabilities
    {
        public const string PluginVersion = "1.1.0";
        public const string AssemblyVersion = "1.1.0.0";
        public const int ContractVersion = 1;

        public static Dictionary<string, object> Describe() => new Dictionary<string, object>
        {
            ["reportVersion"] = 1,
            ["evidenceKind"] = "compiled-capabilities",
            ["pluginId"] = "com.buele.aicresourcekit",
            ["pluginVersion"] = PluginVersion,
            ["contractVersion"] = ContractVersion,
            ["contractStatus"] = "stable",
            ["targetGameBaseline"] = "ver030g",
            ["manifestVersions"] = new[] { ResourceManifest.FormatVersion },
            ["encryption"] = new Dictionary<string, object>
            {
                ["format"] = "BEREENC", ["versions"] = new[] { 1 },
                ["mixedPlaintext"] = true, ["changesManifestPaths"] = false
            },
            ["identityComparison"] = "ordinal-case-sensitive",
            ["priority"] = "later-enabled-pack-wins-or-composes",
            ["dependencyFields"] = new[] { "image", "pages[].image", "atlas", "spine.json" },
            ["features"] = new object[]
            {
                Feature("texture-mti", true, true, "single-image-and-direct-image", "original-dimensions", "legacy-null-wildcard-and-exact-empty-key"),
                Feature("texture-resources", true, true, "Texture2D", "unpacked-Sprite", "original-dimensions-and-geometry"),
                Feature("texture-pxl", true, true, "embedded-I-P", "external-and-packed-pages", "original-dimensions", "shared-texture-conflicts-rejected"),
                Feature("spine", true, true, "Spine-4.1-JSON", "single-page", "sections-and-compatibility", "portrait-preview"),
                Feature("spine-assets", true, true, "mti-or-resources", "explicit-multiple-pages", "per-viewer-materials", "preserve-playback"),
                Feature("atlas-region", true, true, "mti-or-resources", "original-full-page-PNG", "copy-packed-rectangle-only", "reject-overlapping-edits"),
                Feature("atlas-page", true, true, "mti-or-resources", "original-dimensions", "preserve-atlas-layout"),
                Feature("mpcc-inspection", true, false, "native-preset-decoder", "registered-PXL-address-mapping", "no-preset-application-or-render-texture-rebuild"),
                Feature("video", false, false, "deferred", "observer-and-address-draft-only")
            },
            ["reports"] = new object[]
            {
                Report("resource-status.json", "current-resource-state"),
                Report("resource-diagnostics.json", "runtime-observation"),
                Report("mpcc-inspection.json", "explicit-inspection", "origin"),
                Report("pack-inventory", "validated-pack-dependencies")
            },
            ["runtimeValidation"] = "not-performed-by-this-report"
        };

        private static Dictionary<string, object> Feature(string id, bool supported, bool installable, params string[] limits)
            => new Dictionary<string, object>
            {
                ["id"] = id, ["supported"] = supported, ["installableTarget"] = installable, ["scopeAndLimits"] = limits
            };

        private static Dictionary<string, object> Report(string file, string kind, string field = "evidenceKind") => new Dictionary<string, object>
            { ["file"] = file, ["reportVersion"] = 1, [field] = kind };
    }
}
