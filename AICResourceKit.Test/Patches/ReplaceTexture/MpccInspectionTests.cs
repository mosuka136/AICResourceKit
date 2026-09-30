using AICResourceKit.Patches.ReplaceTexture;
using System.Text;

namespace AICResourceKit.Test.Patches.ReplaceTexture
{
    public sealed class MpccInspectionTests
    {
        [Fact]
        public void Header_UsesEmbeddedCharacterAndAllowsEmptyPreset()
        {
            var value = MpccFileHeader.Read(Header("palette", "NOEL", 0));
            Assert.Equal("palette", value.Name);
            Assert.Equal("NOEL", value.Character);
            Assert.Equal(0, value.PartCount);
            Assert.Equal(2, MpccFileHeader.Read(Header("调色", "sub_i", 2)).PartCount);
        }

        [Fact]
        public void Header_RejectsTruncationUnsupportedVersionsAndInvalidUtf8()
        {
            var bytes = Header("a", "NOEL", 1);
            Assert.Throws<InvalidDataException>(() => MpccFileHeader.Read(null));
            Assert.Throws<InvalidDataException>(() => MpccFileHeader.Read(bytes.Take(5).ToArray()));
            Assert.Throws<InvalidDataException>(() => MpccFileHeader.Read(bytes.Take(bytes.Length - 1).ToArray()));
            bytes[0] = 1;
            Assert.Throws<InvalidDataException>(() => MpccFileHeader.Read(bytes));
            bytes[0] = 0; bytes[bytes.Length - 2] = 1;
            Assert.Throws<InvalidDataException>(() => MpccFileHeader.Read(bytes));
            bytes[bytes.Length - 2] = 0; bytes[3] = 255;
            Assert.Throws<DecoderFallbackException>(() => MpccFileHeader.Read(bytes));
        }

        [Fact]
        public void Mapping_RequiresActualSourceAndNoelModeMembership()
        {
            var noel = new[] { "noel", "noel_walk" };
            Assert.True(MpccFileHeader.MatchesSource("NOEL", "PxlNoel/noel.pxls", "noel", noel));
            Assert.False(MpccFileHeader.MatchesSource("NOEL", "MapChars/noel.pxls", "noel", noel));
            Assert.False(MpccFileHeader.MatchesSource("NOEL", "PxlNoel/not_noel.pxls", "not_noel", noel));
            Assert.False(MpccFileHeader.MatchesSource("noel", "PxlNoel/noel.pxls", "noel", noel));
            Assert.True(MpccFileHeader.MatchesSource("sub_i", "MapChars/sub_i.pxls", "sub_i", noel));
            Assert.False(MpccFileHeader.MatchesSource("sub_i", "MapChars/sub_a.pxls", "sub_i", noel));
            Assert.False(MpccFileHeader.MatchesSource(null, "MapChars/sub_i.pxls", "sub_i", noel));
        }

        private static byte[] Header(string name, string character, byte count)
        {
            var bytes = new List<byte> { 0 };
            foreach (string value in new[] { name, character })
            {
                var encoded = Encoding.UTF8.GetBytes(value);
                bytes.Add((byte)(encoded.Length >> 8)); bytes.Add((byte)encoded.Length); bytes.AddRange(encoded);
            }
            bytes.Add(0); bytes.Add(count);
            return bytes.ToArray();
        }
    }
}
