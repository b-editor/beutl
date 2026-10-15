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

    // PNG integers are big-endian, so this chunk must read as 64x48 and write back to the same bytes.
    // RawData goes through BitConverter, so this only holds on little-endian hosts.
    [Test]
    public void IHDRChunk_ParsesBigEndianDimensionsAndRoundTripsToTheSameBytes()
    {
        Assume.That(BitConverter.IsLittleEndian);

        byte[] raw = BuildIhdrChunk(64, 48);

        var chunk = new IHDRChunk(new MemoryStream(raw));

        Assert.That(chunk.ChunkType, Is.EqualTo("IHDR"));
        Assert.That(chunk.Width, Is.EqualTo(64));
        Assert.That(chunk.Height, Is.EqualTo(48));
        Assert.That(chunk.Width, Is.Not.EqualTo(0x40000000));
        Assert.That(chunk.RawData, Is.EqualTo(raw));
    }
}
