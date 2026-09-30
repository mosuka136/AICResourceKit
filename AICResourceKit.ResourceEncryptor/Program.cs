using AICResourceKit.Contracts;
using System.Text.Json;

namespace AICResourceKit.ResourceEncryptor
{
    internal static class Program
    {
        private const string Usage = "Usage: AICResourceKit.ResourceEncryptor encrypt --input <resource-root> --output <new-directory>\n"
            + "       AICResourceKit.ResourceEncryptor inspect --input <resource-root>\n"
            + "       AICResourceKit.ResourceEncryptor capabilities";

        private static int Main(string[] args) => Run(args, Console.Out, Console.Error);

        private static void WriteJson(TextWriter output, Dictionary<string, object> value) =>
            output.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));

        internal static int Run(string[] args, TextWriter output, TextWriter error)
        {
            if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h"))
            {
                output.WriteLine(Usage);
                return 0;
            }
            if (args.Length == 1 && args[0] == "capabilities")
            {
                WriteJson(output, ResourceCapabilities.Describe());
                return 0;
            }
            if (args.Length == 3 && args[0] == "inspect" && args[1] == "--input" && !string.IsNullOrWhiteSpace(args[2]))
            {
                try { WriteJson(output, PackInventory.Read(args[2]).Describe()); return 0; }
                catch (Exception ex) { error.WriteLine("Inspection failed: " + ex.Message); return 1; }
            }
            if (args.Length != 5 || args[0] != "encrypt")
            {
                error.WriteLine(Usage);
                return 2;
            }
            string input = null, destination = null;
            for (int i = 1; i < args.Length; i += 2)
            {
                if (args[i] == "--input" && input == null) input = args[i + 1];
                else if (args[i] == "--output" && destination == null) destination = args[i + 1];
                else { error.WriteLine(Usage); return 2; }
            }
            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(destination))
            {
                error.WriteLine(Usage);
                return 2;
            }
            try
            {
                int count = PackEncryptor.Encrypt(input, destination, path => output.WriteLine("Encrypted: " + path));
                output.WriteLine("Completed: " + count + " files -> " + Path.GetFullPath(destination));
                return 0;
            }
            catch (Exception ex)
            {
                error.WriteLine("Encryption failed: " + ex.Message);
                return 1;
            }
        }
    }
}
