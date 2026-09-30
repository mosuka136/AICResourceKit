using AICResourceKit.Contracts;
using AICResourceKit.Patches.ReplaceTexture;
using System.Security.Cryptography;

namespace AICResourceKit.ResourceEncryptor
{
    // 检查和加密共用同一份依赖清单，保持跨包共享文件、路径与 Sensitive 语义一致。
    internal sealed class PackInventory
    {
        internal readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private readonly List<object> manifests = new List<object>();
        private readonly string root;

        private PackInventory(string root) { this.root = root; }

        internal static PackInventory Read(string input)
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
            ReplacementResourcePaths.CheckAncestors(root);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Input directory does not exist: " + root);
            var result = new PackInventory(root);
            var files = Enumerate(root).Where(path => path.EndsWith(".replacement.json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            if (files.Length == 0) throw new InvalidDataException("No v2 .replacement.json manifests were found.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            string sensitive = Path.Combine(root, "Sensitive");
            foreach (string file in files)
            {
                try
                {
                    var manifest = ResourceManifest.Parse(ManifestJson.Parse(ReplacementResourceIO.ReadText(file)));
                    if (!ids.Add(manifest.Id)) throw new InvalidDataException("Duplicate replacement id: " + manifest.Id);
                    result.Add(file);
                    var targets = new List<object>();
                    for (int i = 0; i < manifest.Targets.Count; i++)
                    {
                        var target = manifest.Targets[i];
                        var dependencies = new List<object>();
                        foreach (var dependency in target.Dependencies)
                        {
                            string path;
                            try { path = result.AddDependency(sensitive, file, dependency); }
                            catch (Exception ex) { throw WithContext("targets[" + i + "] " + dependency.Path, ex); }
                            dependencies.Add(new Dictionary<string, object>
                            {
                                ["kind"] = dependency.Kind, ["path"] = result.Relative(path), ["pageKey"] = dependency.PageKey
                            });
                        }
                        targets.Add(new Dictionary<string, object>
                        {
                            ["targetIndex"] = i, ["type"] = target.Type, ["runtimeIdentity"] = target.Identity, ["dependencies"] = dependencies
                        });
                    }
                    result.manifests.Add(new Dictionary<string, object>
                    {
                        ["id"] = manifest.Id, ["manifest"] = result.Relative(file),
                        ["sensitive"] = ReplacementResourcePaths.Within(sensitive, file), ["targets"] = targets
                    });
                }
                catch (Exception ex) { throw WithContext(result.Relative(file), ex); }
            }
            return result;
        }

        internal Dictionary<string, object> Describe() => new Dictionary<string, object>
        {
            ["reportVersion"] = 1, ["evidenceKind"] = "validated-pack-dependencies",
            ["pluginVersion"] = ResourceCapabilities.PluginVersion, ["contractVersion"] = ResourceCapabilities.ContractVersion,
            ["manifests"] = manifests, ["fileCount"] = Files.Count,
            ["files"] = Files.Keys.Select(Relative).OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            ["runtimeValidation"] = "not-performed"
        };

        private string Relative(string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

        private static Exception WithContext(string context, Exception error)
        {
            string message = context + ": " + error.Message;
            if (error is FileNotFoundException missing) return new FileNotFoundException(message, missing.FileName, error);
            if (error is IOException) return new IOException(message, error);
            return new InvalidDataException(message, error);
        }

        private string AddDependency(string sensitive, string manifest, ResourceDependency dependency)
        {
            string path = ReplacementResourcePaths.Resolve(root, Path.GetDirectoryName(manifest), dependency.Path);
            if (ReplacementResourcePaths.Within(sensitive, path) != ReplacementResourcePaths.Within(sensitive, manifest))
                throw new InvalidDataException("A package and all dependencies must stay in the same normal or Sensitive tree.");
            if (!File.Exists(path)) throw new FileNotFoundException("Replacement resource was not found.", path);
            if (dependency.Kind == "image")
            {
                var header = ReplacementResourceIO.ReadPrefix(path, 33);
                byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
                if (header.Length < 33 || !header.Take(8).SequenceEqual(signature)
                    || header[12] != 73 || header[13] != 72 || header[14] != 68 || header[15] != 82)
                    throw new InvalidDataException("Expected PNG with IHDR: " + dependency.Path);
            }
            else if (dependency.Kind == "json") ManifestJson.Parse(ReplacementResourceIO.ReadText(path));
            else if (string.IsNullOrWhiteSpace(ReplacementResourceIO.ReadText(path)))
                throw new InvalidDataException("Empty atlas: " + dependency.Path);
            Add(path);
            return path;
        }

        private void Add(string path) { if (!Files.ContainsKey(path)) Files.Add(path, Hash(path)); }

        internal static byte[] Hash(string path)
        {
            using (var stream = ReplacementResourceIO.OpenRead(path))
            using (var sha = SHA256.Create()) return sha.ComputeHash(stream);
        }

        internal static IEnumerable<string> Enumerate(string directory)
        {
            ReplacementResourcePaths.RejectLink(directory);
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                ReplacementResourcePaths.RejectLink(path);
                if (Directory.Exists(path))
                {
                    foreach (string child in Enumerate(path)) yield return child;
                }
                else yield return path;
            }
        }
    }
}
