using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AICResourceKit.Patches.ReplaceTexture
{
    internal sealed class ReplacementManifestInfo
    {
        internal string Id, Path;
        internal bool Sensitive;
    }

    internal sealed class ReplacementIssue
    {
        internal string Manifest, PackageId, Identity, File, Code, Reason;
        internal int? TargetIndex;

        internal static ReplacementIssue Create(string manifest, string packageId, int? targetIndex, string identity, Exception error)
        {
            string message = error.Message;
            string code = error is FileNotFoundException ? "missing-dependency"
                : message.StartsWith("Unsupported replacement manifest version.", StringComparison.Ordinal) ? "unsupported-version"
                : message.StartsWith("Target type must be ", StringComparison.Ordinal)
                    || message.StartsWith("Texture loader must be ", StringComparison.Ordinal)
                    || message.StartsWith("Resources objectType must be ", StringComparison.Ordinal) ? "unsupported-target"
                : targetIndex.HasValue ? "invalid-target" : "invalid-manifest";
            return new ReplacementIssue { Manifest = manifest, PackageId = packageId, TargetIndex = targetIndex,
                Identity = identity, File = (error as FileNotFoundException)?.FileName, Code = code, Reason = message };
        }

        internal Dictionary<string, object> Describe(Func<string, string> path) => new Dictionary<string, object>
        {
            ["manifest"] = path(Manifest), ["packageId"] = PackageId, ["targetIndex"] = TargetIndex,
            ["identity"] = Identity, ["file"] = path(File), ["code"] = Code, ["reason"] = path(Reason),
            ["status"] = Code.StartsWith("unsupported-", StringComparison.Ordinal) ? "unsupported" : "failed"
        };
    }

    // 当前对象快照；不从历史诊断的“曾应用”推断当前状态。
    internal sealed class ReplacementLoadStatus
    {
        internal int Loaded, Applied, Preparing;
        internal readonly HashSet<string> AppliedPackages = new HashSet<string>(StringComparer.Ordinal);
        internal readonly List<string> Errors = new List<string>();

        internal void Observe(bool applied, bool preparing, IEnumerable<string> packages = null)
        {
            Loaded++;
            if (applied) Applied++;
            if (preparing) Preparing++;
            if (applied && packages != null)
                foreach (string id in packages) AppliedPackages.Add(id);
        }

        internal string State(bool enabled, bool authorized)
        {
            if (!enabled) return "disabled";
            if (!authorized) return "unauthorized";
            if (Errors.Count > 0) return "failed";
            if (Preparing > 0) return "preparing";
            if (Applied > 0) return Applied == Loaded ? "applied" : "partial";
            return Loaded > 0 ? "loaded" : "not-loaded";
        }
    }
}
