using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using PixelLiner;
using PixelLiner.PixelLinerLib;
using UnityEngine;
using XX.mobpxl;

namespace AICResourceKit.Patches.ReplaceTexture
{
    internal static class MpccInspection
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo palette = typeof(MobPCCContainer).GetField("OACC", Fields);
        private static readonly MethodInfo decode = typeof(MobPCCContainer).GetMethod("readFromBytesFromFile", Fields);
        internal static string InspectingFile { get; private set; }

        internal static Dictionary<string, object> Describe(MobPCCContainer container)
        {
            var parts = new List<object>();
            foreach (DictionaryEntry entry in (IDictionary)palette.GetValue(container))
            {
                var operations = new List<object>();
                foreach (MobPCC operation in (IEnumerable)entry.Value.GetType().GetField("A", Fields).GetValue(entry.Value))
                {
                    var detail = new Dictionary<string, object> { ["type"] = operation.type.ToString(), ["visible"] = operation.visible };
                    if (operation is MobPCCHsv hsv)
                    { detail["h"] = hsv.h; detail["s"] = hsv.s; detail["v"] = hsv.v; detail["flags"] = hsv.flags; }
                    operations.Add(detail);
                }
                parts.Add(new Dictionary<string, object> { ["name"] = entry.Key, ["operations"] = operations });
            }
            return new Dictionary<string, object>
            {
                ["name"] = container.name, ["characterKey"] = container.chr_name, ["parts"] = parts,
                ["emptyPalette"] = parts.Count == 0, ["embeddedImages"] = false
            };
        }

        internal static Dictionary<string, object> ReadFile(string path)
        {
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("MPCC file exceeds 1 MiB.");
            byte[] bytes = File.ReadAllBytes(path);
            var header = MpccFileHeader.Read(bytes);
            var container = new MobPCCContainer();
            string previous = InspectingFile;
            InspectingFile = Path.GetFileName(path);
            try
            {
                var reader = new ByteArray(bytes);
                decode.Invoke(container, new object[] { reader, null, null });
                if (reader.position != reader.Length) throw new InvalidDataException("MPCC has unread or incomplete data.");
                var details = Describe(container);
                var parts = ((List<object>)details["parts"]).Cast<Dictionary<string, object>>().Select(part => (string)part["name"]).ToArray();
                if (parts.Length != header.PartCount) throw new InvalidDataException("MPCC contains empty or duplicate part entries.");
                details["file"] = Path.GetFileName(path);
                details["byteLength"] = bytes.Length;
                using (var hash = SHA256.Create()) details["sha256"] = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                details["formatVersion"] = 0;
                details["origin"] = "explicit-inspection";
                details["automaticGameLoad"] = "not-established";
                details["dependencies"] = ReplacementRuntime.DescribeMpccDependencies(header.Character, parts);
                details["status"] = "decoded";
                return details;
            }
            catch (TargetInvocationException ex) { throw new InvalidDataException("Game MPCC decoder failed.", ex.InnerException ?? ex); }
            finally { InspectingFile = previous; container.destruct(); }
        }

        internal static string Export()
        {
            string directory = Path.Combine(Application.streamingAssetsPath, "mobpcc");
            var files = new List<object>();
            foreach (string path in Directory.GetFiles(directory, "*.mpcc.bytes").OrderBy(value => value, StringComparer.Ordinal))
            {
                try { files.Add(ReadFile(path)); }
                catch (Exception ex) { files.Add(new Dictionary<string, object>
                    { ["file"] = Path.GetFileName(path), ["status"] = "failed", ["reason"] = ReplacementDiagnosticRuntime.Sanitize(ex.Message) }); }
            }
            var report = new Dictionary<string, object>
            {
                ["reportVersion"] = 1, ["origin"] = "explicit-inspection", ["sourceDirectory"] = "StreamingAssets/mobpcc",
                ["gameAssemblyMvid"] = typeof(MobPCCContainer).Assembly.ManifestModule.ModuleVersionId.ToString(),
                ["replacementApplied"] = false, ["files"] = files
            };
            Directory.CreateDirectory(PatchInfo.LoggerPath);
            string output = Path.Combine(PatchInfo.LoggerPath, "mpcc-inspection.json");
            File.WriteAllText(output, PortraitJson.Serialize(report) + "\r\n", new UTF8Encoding(false));
            return output;
        }
    }
}
