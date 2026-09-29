using AICResourceKit.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace AICResourceKit.Patches.ReplaceTexture
{
    internal sealed class ReplacementTarget : ResourceTarget
    {
        internal ReplacementPackage Owner;
        internal string PackageId;
        internal string ImagePath;
        internal string AtlasPath;
        internal string JsonPath;
    }

    internal sealed class ReplacementPackage
    {
        internal string Id;
        internal string ManifestPath;
        internal bool Sensitive;
        internal readonly List<ReplacementTarget> Targets = new List<ReplacementTarget>();
        internal readonly HashSet<string> InvalidTargetIdentities = new HashSet<string>(StringComparer.Ordinal);
        internal bool HasUnidentifiedTargetErrors;
    }

    internal sealed class ReplacementCatalog
    {
        internal readonly List<ReplacementPackage> Packages = new List<ReplacementPackage>();
        internal readonly List<string> Errors = new List<string>();
        // 磁盘上所有清单声明的 id，包含未授权的敏感包、解析失败的包和 id 重复的包。
        // 配置行是否“对应的包已不存在”只依据这个集合，与敏感授权和解析结果无关。
        internal readonly HashSet<string> DeclaredIds = new HashSet<string>(StringComparer.Ordinal);
        // 存在读不出 id 的清单时为 false：此时无法判定未知配置行归属，不能删除任何未知行。
        internal bool DeclaredIdsComplete = true;

        internal static ReplacementCatalog Discover(string root, string sensitive, bool allowSensitive,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = new ReplacementCatalog();
            if (!Directory.Exists(root)) return result;
            var verified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Enumerate(root).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!file.EndsWith(".replacement.json", StringComparison.OrdinalIgnoreCase)) continue;
                // 先单独读出清单声明的 id 再做完整解析：敏感包被关闭或清单解析失败时也要登记 id，
                // 否则这些包的配置行会被误判成“包已被删除”而清除。
                Dictionary<string, object> json = null;
                Exception unreadable = null;
                string declared = null;
                try
                {
                    json = PortraitJson.Parse(ReplacementResourceIO.ReadText(file));
                    declared = PortraitJson.String(json, "id")?.Trim();
                }
                catch (Exception ex) { unreadable = ex; }
                if (string.IsNullOrEmpty(declared)) result.DeclaredIdsComplete = false;
                else result.DeclaredIds.Add(declared);
                try
                {
                    bool isSensitive = PortraitCatalog.Within(sensitive, file);
                    if (isSensitive && !allowSensitive) continue;
                    if (json == null)
                        throw unreadable ?? new InvalidDataException("Replacement manifest could not be read.");
                    var package = ParsePackage(root, sensitive, file, isSensitive, verified, result.Errors, json);
                    result.Packages.Add(package);
                }
                catch (Exception ex) { result.Errors.Add(file + ": " + ex.Message); }
            }
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var duplicate in result.Packages.GroupBy(package => package.Id, StringComparer.Ordinal)
                .Where(group => group.Count() > 1).ToList())
            {
                result.Errors.Add("Duplicate replacement id: " + duplicate.Key);
                result.Packages.RemoveAll(package => package.Id == duplicate.Key);
            }
            return result;
        }

        internal IEnumerable<ReplacementTarget> Layers(string identity, IReadOnlyList<string> enabledIds)
        {
            var byId = Packages.ToDictionary(package => package.Id, StringComparer.Ordinal);
            foreach (string id in enabledIds)
                if (byId.TryGetValue(id, out var package))
                    foreach (var target in package.Targets)
                        if (target.Identity == identity) yield return target;
        }

        internal static List<string> EnabledIds(IEnumerable<(string Id, bool Enabled)> rows)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows ?? Array.Empty<(string, bool)>())
            {
                string id = row.Id?.Trim();
                if (row.Enabled && !string.IsNullOrEmpty(id) && seen.Add(id)) result.Add(id);
            }
            return result;
        }

        // 以磁盘上仍然存在的清单 id 为准同步配置行：已授权且解析成功的包（discovered）与仅登记了 id 的包
        // （未授权的敏感包、解析失败的包、id 重复的包）都算存在，对应行连同用户开关一起保留；
        // 扫描不到的 id 视为包已被删除，该行直接从配置中清除。存在读不出 id 的清单时无法判定未知行归属，
        // 整轮退化为只追加不删除。
        internal List<(string Id, bool Enabled)> SyncRows(IEnumerable<(string Id, bool Enabled)> current)
        {
            return SyncRows(Packages, current, DeclaredIdsComplete, DeclaredIds);
        }

        internal static List<(string Id, bool Enabled)> SyncRows(IReadOnlyList<ReplacementPackage> discovered,
            IEnumerable<(string Id, bool Enabled)> current, bool dropUnmatched, IEnumerable<string> declaredIds = null)
        {
            var known = new HashSet<string>(discovered.Select(package => package.Id), StringComparer.Ordinal);
            foreach (string id in declaredIds ?? Array.Empty<string>())
                if (!string.IsNullOrEmpty(id)) known.Add(id);
            var rows = new List<(string, bool)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in current ?? Array.Empty<(string, bool)>())
            {
                string id = row.Id?.Trim();
                if (string.IsNullOrEmpty(id)) { rows.Add(row); continue; }
                if (known.Contains(id) && seen.Add(id)) rows.Add((id, row.Enabled));
                else if (!known.Contains(id) && !dropUnmatched) rows.Add(row);
            }
            int blank = rows.FindIndex(row => string.IsNullOrWhiteSpace(row.Item1));
            if (blank < 0) blank = rows.Count;
            foreach (var package in discovered)
                if (seen.Add(package.Id)) rows.Insert(blank++, (package.Id, false));
            return rows;
        }

        private static ReplacementPackage ParsePackage(string root, string sensitive, string file, bool isSensitive,
            HashSet<string> verified, List<string> errors, Dictionary<string, object> json)
        {
            string id = ResourceManifest.ReadHeader(json, out var items);
            var package = new ReplacementPackage
            {
                Id = id, ManifestPath = file, Sensitive = isSensitive
            };
            string directory = Path.GetDirectoryName(file);
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (object item in items)
            {
                Dictionary<string, object> targetJson;
                string identity;
                try
                {
                    targetJson = PortraitJson.Object(item);
                    identity = ResourceManifest.IdentityOf(targetJson);
                }
                catch (Exception ex)
                {
                    package.HasUnidentifiedTargetErrors = true;
                    errors.Add(file + " [invalid target]: " + ex.Message);
                    continue;
                }

                // 身份重复使整个包无效，独立于下面的单目标文件和内容检查。
                if (!identities.Add(identity))
                    throw new InvalidDataException("Duplicate target in package: " + identity.Replace('\n', '/'));
                try
                {
                    var target = ParseTarget(root, sensitive, directory, package.Id, isSensitive, targetJson, verified);
                    target.Owner = package;
                    package.Targets.Add(target);
                }
                catch (Exception ex)
                {
                    package.InvalidTargetIdentities.Add(identity);
                    errors.Add(file + " [" + identity + "]: " + ex.Message);
                }
            }
            return package;
        }

        private static ReplacementTarget ParseTarget(string root, string sensitive, string directory, string packageId,
            bool packageSensitive, Dictionary<string, object> json, HashSet<string> verified)
        {
            var target = ResourceManifest.ReadTarget<ReplacementTarget>(json);
            target.PackageId = packageId;
            foreach (var dependency in target.Dependencies)
            {
                string path = Resource(root, sensitive, directory, dependency.Path, packageSensitive, verified);
                if (dependency.Kind == "image")
                {
                    target.ImagePath = path;
                    ValidatePngHeader(path);
                }
                else if (dependency.Kind == "atlas")
                {
                    target.AtlasPath = path;
                    if (PortraitCatalog.ReadAtlas(ReplacementResourceIO.ReadText(path)).Pages.Count != 1)
                        throw new InvalidDataException("Spine replacement atlas must contain exactly one page.");
                }
                else
                {
                    target.JsonPath = path;
                    PortraitJson.Parse(ReplacementResourceIO.ReadText(path));
                }
            }
            return target;
        }

        private static string Resource(string root, string sensitive, string directory, string relative,
            bool packageSensitive, HashSet<string> verified)
        {
            string path = PortraitCatalog.Resolve(root, directory, relative, verified);
            if (PortraitCatalog.Within(sensitive, path) != packageSensitive)
                throw new InvalidDataException("A package and all dependencies must stay in the same normal or Sensitive tree.");
            if (!File.Exists(path)) throw new FileNotFoundException("Replacement resource was not found.", path);
            return path;
        }

        private static void ValidatePngHeader(string path)
        {
            byte[] header = ReplacementResourceIO.ReadPrefix(path, 33);
            if (header.Length != 33) throw new InvalidDataException("Expected PNG with IHDR.");
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (!header.Take(8).SequenceEqual(signature)) throw new InvalidDataException("Expected PNG with IHDR.");
        }

        private static IEnumerable<string> Enumerate(string directory)
        {
            foreach (string file in Directory.GetFiles(directory))
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) yield return file;
            foreach (string child in Directory.GetDirectories(directory))
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    foreach (string file in Enumerate(child)) yield return file;
        }
    }
}
