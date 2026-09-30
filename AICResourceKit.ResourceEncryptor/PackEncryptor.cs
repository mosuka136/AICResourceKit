using AICResourceKit.Patches.ReplaceTexture;

namespace AICResourceKit.ResourceEncryptor
{
    internal static class PackEncryptor
    {
        internal static int Encrypt(string input, string output, Action<string> progress = null)
        {
            input = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
            output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
            if (ReplacementResourcePaths.Within(input, output) || ReplacementResourcePaths.Within(output, input))
                throw new InvalidDataException("Input and output directories must not overlap.");
            ReplacementResourcePaths.CheckAncestors(input);
            ReplacementResourcePaths.CheckAncestors(output);
            if (!Directory.Exists(input)) throw new DirectoryNotFoundException("Input directory does not exist: " + input);
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output already exists: " + output);
            string parent = Path.GetDirectoryName(output);
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("Output parent directory does not exist: " + parent);

            var sources = PackInventory.Read(input).Files;
            // 暂存目录与最终目录在同一父目录中，所有文件回读一致后才发布。
            string staging = Path.Combine(parent, ".be-encrypt-" + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(staging) || File.Exists(staging)) throw new IOException("Temporary output already exists.");
            Directory.CreateDirectory(staging);
            try
            {
                foreach (var source in sources.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    string relative = Path.GetRelativePath(input, source.Key);
                    string path = ReplacementResourcePaths.Resolve(input, input, relative);
                    string destination = ReplacementResourcePaths.Resolve(staging, staging, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    ReplacementResourcePaths.CheckAncestors(destination);
                    using (var decoded = ReplacementResourceIO.OpenRead(path))
                    using (var encoded = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                        ReplacementResourceIO.Encrypt(decoded, encoded);
                    if (!PackInventory.Hash(destination).SequenceEqual(source.Value))
                        throw new InvalidDataException("Resource changed or encrypted output did not round-trip: " + relative);
                    progress?.Invoke(relative);
                }
                ReplacementResourcePaths.CheckAncestors(output);
                Directory.Move(staging, output);
                return sources.Count;
            }
            finally
            {
                if (Directory.Exists(staging)) RemoveStaging(staging, parent);
            }
        }

        private static void RemoveStaging(string staging, string parent)
        {
            string full = Path.GetFullPath(staging);
            if (!string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(parent), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(full).StartsWith(".be-encrypt-", StringComparison.Ordinal))
                throw new IOException("Refusing to remove an unexpected temporary directory: " + full);
            ReplacementResourcePaths.CheckAncestors(full);
            // 删除前检查完整目录树，不跟随目录链接；这里只删除本次创建的暂存目录。
            foreach (string path in PackInventory.Enumerate(full)) ReplacementResourcePaths.RejectLink(path);
            Directory.Delete(full, true);
        }
    }
}
