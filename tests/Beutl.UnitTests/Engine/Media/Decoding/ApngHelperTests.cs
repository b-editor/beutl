using Beutl.Media.Decoding.APNG;
using Beutl.Media.Decoding.APNG.Chunks;

namespace Beutl.UnitTests.Engine.Media.Decoding;

[TestFixture]
public class ApngHelperTests
{
    private static byte[] BuildIhdrChunk(int width, int height)
    {
        byte[] data =
        [
            (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
            (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
            8, // bit depth
            6, // colour type: truecolour with alpha
            0, // compression method
            0, // filter method
            0, // interlace method
        ];

        byte[] type = "IHDR"u8.ToArray();
        uint crc = CrcHelper.Calculate([.. type, .. data]);

        return
        [
            0, 0, 0, (byte)data.Length,
            .. type,
            .. data,
            (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc,
        ];
    }

    // End-to-end control through the decoder: PNG writes every integer big-endian, so this chunk must
    // parse as 64x48 and re-serialize to the exact bytes it came from. With the reversal removed the
    // 13-byte length field parses as 0x0D000000 and Chunk's constructor throws "End reached." before
    // Width is even read.
    //
    // RawData writes through BitConverter after reversing, which is host-endianness dependent and therefore
    // only defined on the little-endian platforms Beutl supports.
    [Test]
    public void IHDRChunk_ParsesBigEndianDimensionsAndRoundTripsToTheSameBytes()
    {
        Assume.That(BitConverter.IsLittleEndian);

        byte[] raw = BuildIhdrChunk(64, 48);

        var chunk = new IHDRChunk(raw);

        Assert.That(chunk.ChunkType, Is.EqualTo("IHDR"));
        Assert.That(chunk.Width, Is.EqualTo(64));
        Assert.That(chunk.Height, Is.EqualTo(48));
        Assert.That(chunk.Width, Is.Not.EqualTo(0x40000000));
        Assert.That(chunk.RawData, Is.EqualTo(raw));
    }
}
