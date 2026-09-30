using System;
using System.IO;
using System.Text;

namespace AICResourceKit.Patches.ReplaceTexture
{
    // 仅检查文件头；调色内容交给游戏自身的读取器，不另写一套 MPCC 解码器。
    internal sealed class MpccFileHeader
    {
        internal string Name, Character;
        internal int PartCount;

        internal static MpccFileHeader Read(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 7 || bytes.Length > 1024 * 1024)
                throw new InvalidDataException("MPCC file must contain a complete header and be at most 1 MiB.");
            int offset = 0;
            if (bytes[offset++] != 0) throw new InvalidDataException("Unsupported MPCC file version.");
            var header = new MpccFileHeader { Name = ReadString(bytes, ref offset), Character = ReadString(bytes, ref offset) };
            if (offset + 2 > bytes.Length) throw new InvalidDataException("Truncated MPCC header.");
            if (bytes[offset++] != 0) throw new InvalidDataException("Unsupported MPCC palette version.");
            header.PartCount = bytes[offset];
            return header;
        }

        private static string ReadString(byte[] bytes, ref int offset)
        {
            if (offset + 2 > bytes.Length) throw new InvalidDataException("Truncated MPCC string length.");
            int length = (bytes[offset] << 8) | bytes[offset + 1]; offset += 2;
            if (offset + length > bytes.Length) throw new InvalidDataException("Truncated MPCC string.");
            string value = new UTF8Encoding(false, true).GetString(bytes, offset, length); offset += length;
            return value;
        }

        // 这是编辑器的加载规则，只有与已登记来源一致时才输出可安装 PXL 地址。
        internal static bool MatchesSource(string character, string assetKey, string title, string[] noelTitles)
        {
            if (character == "NOEL")
                return assetKey == "PxlNoel/" + title + ".pxls" && Array.IndexOf(noelTitles ?? Array.Empty<string>(), title) >= 0;
            if (string.IsNullOrEmpty(character)) return false;
            string path = character.IndexOf('/') < 0 ? "MapChars/" + character : character;
            return title == character && assetKey == path + ".pxls";
        }
    }
}
