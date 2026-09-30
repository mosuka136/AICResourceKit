using AICResourceKit.Contracts;
using AICResourceKit.BConfigManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityModBase.HTranslatorSpace;

namespace AICResourceKit.Patches.ReplaceTexture
{
    internal static partial class ReplacementRuntime
    {
        private static readonly Dictionary<string, string> statusFailures = new Dictionary<string, string>(StringComparer.Ordinal);
        private static string scanError;
        internal static string StatusFilter = "";

        // 只记本轮失败原因；数量受本轮已登记/已选目标约束，不复制可选诊断的事件历史。
        internal static void TrackResourceResult(string identity, string stage, string reason)
        {
            if (identity == null) return;
            if (stage == "candidate-failed") statusFailures[identity] = reason ?? "Resource application failed.";
            else if (stage == "candidate-pending" || stage == "candidate-applied" || stage == "released") statusFailures.Remove(identity);
        }

        private static void AddStatusError(ReplacementLoadStatus status, string identity)
        {
            if (identity != null && statusFailures.TryGetValue(identity, out var reason) && !status.Errors.Contains(reason))
                status.Errors.Add(reason);
        }

        private static ReplacementLoadStatus ReadLoadStatus(ReplacementTarget target)
        {
            var result = new ReplacementLoadStatus();
            string identity = target.Identity;
            if (target.Type == "spine")
            {
                if (!spineAvailable) result.Errors.Add("The game's portrait interfaces are unavailable.");
                foreach (var pair in spineStates.Where(pair => pair.Key.key == target.SpineKey
                    && (pair.Value.JsonKey == target.JsonKey || pair.Value.PendingKey == target.JsonKey)))
                {
                    var state = pair.Value;
                    // 临时预览沿用原行为；显示的是预览时不把后台普通组合算作正在显示。
                    var shown = state.ShownKey == target.JsonKey ? state.Shown : null;
                    result.Observe(shown != null, state.Pending != null, shown?.Sources.Select(package => package.Id));
                }
            }
            else if (target.Type == "spine-assets")
            {
                foreach (var state in ordinaryViewers.Values.Where(state => state.Source.Address.Identity == identity))
                    result.Observe(state.Current != null, state.Pending != null, state.Current?.Sources.Select(package => package.Id));
            }
            else if (target.AtlasAddress != null)
            {
                foreach (var binding in atlasBindings.Values.Where(binding => binding.Asset != null
                    && target.AtlasAddress.SameAtlas(binding.Source.Address(target.AtlasAddress.MemberKey, target.AtlasAddress.IsPage))))
                {
                    try
                    {
                        var layout = AtlasEditLayout.Resolve(binding.Metadata, target.AtlasAddress);
                        if (binding.Pages.TryGetValue(layout.Page.name, out var page) && page.Surface?.Texture != null)
                        {
                            var surface = page.Surface;
                            var applied = surface.Applied?.Where(edit => edit.Target.Identity == identity).Select(edit => edit.Target.PackageId).ToArray();
                            result.Observe(applied?.Length > 0, surface.Pending != null, applied);
                            AddStatusError(result, binding.Source.Address(layout.Page.name, true).Identity);
                        }
                    }
                    catch (Exception ex) { result.Errors.Add(ex.Message); }
                }
            }
            else if (target.Loader == "pxl")
            {
                var surfaces = pxlCharacters.Values.SelectMany(character => character.Bindings.Values)
                    .Where(binding => binding.Address.Identity == identity && binding.Surface?.Texture != null)
                    .Select(binding => binding.Surface).Distinct();
                foreach (var surface in surfaces)
                {
                    bool applied = surface.Applied?.Identity == identity;
                    result.Observe(applied, surface.Pending != null, applied ? new[] { surface.Applied.PackageId } : null);
                }
            }
            else if (target.Loader == "mti")
            {
                foreach (var record in AllMtiRecords().Where(record => record.Image?.Tx != null
                    && ResourceIdentity.MatchesMti(target.AssetKey, target.ImageKey, record.AssetKey, record.ImageKey)))
                {
                    bool applied = record.SourceIdentity == identity && record.Replacement != null;
                    result.Observe(applied, record.Pending != null, applied ? new[] { record.Source.Id } : null);
                    AddStatusError(result, ResourceIdentity.Mti(record.AssetKey, record.ImageKey));
                }
            }
            else
            {
                foreach (var record in resourceRecords.Values.Where(record => record.Original != null
                    && record.Path == target.ResourcePath && record.ObjectType == target.ObjectType))
                {
                    bool applied = record.SourceIdentity == identity && record.Source != null;
                    result.Observe(applied, record.Pending != null, applied ? new[] { record.Source.Id } : null);
                }
            }
            AddStatusError(result, identity);
            return result;
        }

        internal static Dictionary<string, object> ResourceStatusSnapshot()
        {
            var enabledIds = EnabledIds();
            bool sensitive = ConfigManager.EnableSensitivities?.Value == true;
            var targets = new List<object>();
            foreach (var group in catalog.Packages.SelectMany(package => package.Targets).GroupBy(target => target.Identity, StringComparer.Ordinal))
            {
                var target = group.First();
                var state = ReadLoadStatus(target);
                var enabled = group.Where(item => enabledIds.Contains(item.PackageId)).ToArray();
                var details = ReplacementDiagnosticTarget.FromManifest(target).Describe();
                details["type"] = target.Type;
                details["status"] = state.State(Enabled && enabled.Length > 0, enabled.Any(item => !item.Owner.Sensitive || sensitive));
                details["loadedCount"] = state.Loaded; details["appliedCount"] = state.Applied; details["preparingCount"] = state.Preparing;
                details["appliedPackageIds"] = state.AppliedPackages.OrderBy(id => id, StringComparer.Ordinal).ToArray();
                details["selectedPackageIds"] = selection.Layers(group.Key).Select(item => item.PackageId).ToArray();
                details["declarations"] = group.Select(item => (object)new Dictionary<string, object>
                {
                    ["packageId"] = item.PackageId, ["manifest"] = ReplacementDiagnosticRuntime.Sanitize(item.Owner.ManifestPath),
                    ["targetIndex"] = item.TargetIndex,
                    ["files"] = new[] { item.ImagePath, item.AtlasPath, item.JsonPath }.Concat(item.PagePaths.Values)
                        .Where(path => path != null).Distinct(StringComparer.Ordinal).Select(ReplacementDiagnosticRuntime.Sanitize).ToArray()
                }).ToArray();
                details["errors"] = state.Errors.Select(ReplacementDiagnosticRuntime.Sanitize).ToArray();
                targets.Add(details);
            }
            var packages = catalog.Manifests.Select(info => (object)new Dictionary<string, object>
            {
                ["id"] = info.Id, ["manifest"] = ReplacementDiagnosticRuntime.Sanitize(info.Path), ["sensitive"] = info.Sensitive,
                ["enabled"] = info.Id != null && enabledIds.Contains(info.Id),
                ["status"] = info.Sensitive && !sensitive ? "unauthorized"
                    : catalog.Issues.Any(issue => issue.Manifest == info.Path) ? "failed"
                    : !Enabled || !enabledIds.Contains(info.Id) ? "disabled" : "enabled"
            }).ToArray();
            return new Dictionary<string, object>
            {
                ["reportVersion"] = 1, ["evidenceKind"] = "current-resource-state", ["pluginVersion"] = PatchInfo.BepInPluginVersion,
                ["exportedUtc"] = DateTime.UtcNow.ToString("O"), ["selectionRevision"] = revision,
                ["replacementEnabled"] = Enabled, ["allowSensitive"] = sensitive,
                ["refreshing"] = scan != null, ["selectionPending"] = !stopped && (selection.Enabled != Enabled || selection.AllowSensitive != sensitive
                    || !selection.EnabledIds.SequenceEqual(enabledIds)),
                ["scanError"] = ReplacementDiagnosticRuntime.Sanitize(scanError),
                ["enabledPackageIds"] = enabledIds.ToArray(), ["packages"] = packages, ["targets"] = targets,
                ["issues"] = catalog.Issues.Select(issue => (object)issue.Describe(ReplacementDiagnosticRuntime.Sanitize)).ToArray(),
                ["visualVerification"] = "not-performed"
            };
        }

        internal static List<string> ResourceStatusLines()
        {
            var snapshot = ResourceStatusSnapshot();
            bool chinese = Translator.DefaultLanguage == LanguageType.Chinese;
            var lines = new List<string>();
            if (scan != null) lines.Add(chinese ? "正在刷新资源目录…" : "Refreshing resource catalog…");
            if (scanError != null) lines.Add((chinese ? "目录刷新失败：" : "Catalog refresh failed: ") + ReplacementDiagnosticRuntime.Sanitize(scanError));
            foreach (Dictionary<string, object> row in (List<object>)snapshot["targets"])
            {
                string label = ((string)row["runtimeIdentity"]).Replace('\n', '/');
                string state = StatusLabel((string)row["status"], chinese);
                string declared = string.Join(", ", ((object[])row["declarations"])
                    .Cast<Dictionary<string, object>>().Select(item => (string)item["packageId"]));
                string applied = string.Join(", ", (string[])row["appliedPackageIds"]);
                string prefix = state + " | " + label + " | " + declared;
                lines.Add(prefix + (applied.Length == 0 ? "" : (chinese ? " | 生效：" : " | applied: ") + applied));
                foreach (string error in (string[])row["errors"]) lines.Add(prefix + " | " + error);
            }
            foreach (Dictionary<string, object> row in (object[])snapshot["packages"])
                if ((string)row["status"] == "unauthorized") lines.Add(StatusLabel("unauthorized", chinese) + " | " + row["id"]);
            foreach (var issue in catalog.Issues)
                lines.Add(StatusLabel(issue.Code.StartsWith("unsupported-", StringComparison.Ordinal) ? "unsupported" : "failed", chinese)
                    + " | " + issue.PackageId + " | " + ReplacementDiagnosticRuntime.Sanitize(issue.Manifest)
                    + (issue.TargetIndex.HasValue ? " [targets[" + issue.TargetIndex + "]]" : "") + " | "
                    + ReplacementDiagnosticRuntime.Sanitize(issue.Reason + (issue.File == null ? "" : " " + issue.File)));
            if (lines.Count == 0) lines.Add(chinese ? "尚未发现资源包。" : "No resource packs discovered.");
            return string.IsNullOrWhiteSpace(StatusFilter) ? lines
                : lines.Where(line => line.IndexOf(StatusFilter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        private static string StatusLabel(string status, bool chinese)
        {
            if (!chinese) return status;
            switch (status)
            {
                case "disabled": return "未启用";
                case "unauthorized": return "未授权";
                case "unsupported": return "不支持";
                case "failed": return "失败";
                case "preparing": return "准备中";
                case "applied": return "已应用";
                case "partial": return "部分应用";
                case "loaded": return "已加载，未应用";
                default: return "未加载";
            }
        }

        internal static string ExportResourceStatus()
        {
            Directory.CreateDirectory(PatchInfo.LoggerPath);
            string output = Path.Combine(PatchInfo.LoggerPath, "resource-status.json");
            File.WriteAllText(output, PortraitJson.Serialize(ResourceStatusSnapshot()) + "\r\n", new UTF8Encoding(false));
            return output;
        }
    }
}
