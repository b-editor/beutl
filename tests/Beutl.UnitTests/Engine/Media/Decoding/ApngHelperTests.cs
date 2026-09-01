using Beutl.Media.Decoding.APNG;
using Beutl.Media.Decoding.APNG.Chunks;

namespace Beutl.UnitTests.Engine.Media.Decoding;

[TestFixture]
public class ApngHelperTests
{
    // The implementation Helper.ConvertEndian had before it moved to BinaryPrimitives:
    // BitConverter.GetBytes -> Array.Reverse -> BitConverter.To*. Kept here as the equivalence
    // oracle so a future change to Helper has to justify itself against the original decoder.
    private static byte[] LegacyReverse(byte[] bytes)
    {
        Array.Reverse(bytes);
        return bytes;
    }

    private static int LegacyConvertEndian(int i) => BitConverter.ToInt32(LegacyReverse(BitConverter.GetBytes(i)), 0);

    private static uint LegacyConvertEndian(uint i) => BitConverter.ToUInt32(LegacyReverse(BitConverter.GetBytes(i)), 0);

    private static short LegacyConvertEndian(short i) => BitConverter.ToInt16(LegacyReverse(BitConverter.GetBytes(i)), 0);

    private static ushort LegacyConvertEndian(ushort i) => BitConverter.ToUInt16(LegacyReverse(BitConverter.GetBytes(i)), 0);

    [TestCase(0x00000000u, 0x00000000u)]
    [TestCase(0x000000FFu, 0xFF000000u)]
    [TestCase(0x0000FF00u, 0x00FF0000u)]
    [TestCase(0x00FF0000u, 0x0000FF00u)]
    [TestCase(0xFF000000u, 0x000000FFu)]
    [TestCase(0xFFFFFFFFu, 0xFFFFFFFFu)]
    [TestCase(0x12345678u, 0x78563412u)]
    [TestCase(0x00000001u, 0x01000000u)]
    public void ConvertEndian_UInt32_SwapsBytes(uint input, uint expected)
    {
        Assert.That(Helper.ConvertEndian(input), Is.EqualTo(expected));
        Assert.That(Helper.ConvertEndian(input), Is.EqualTo(LegacyConvertEndian(input)));
    }

    // The signed overload is where a naive swap goes wrong: the sign bit moves between the high and low
    // byte. Five of these carry the value across zero in one direction or the other - 0x000000FF,
    // 0xFF000000, int.MinValue, int.MaxValue and 0x80000001 - and the rest hold the reversal to the byte
    // pattern for inputs whose sign it leaves where it was.
    [TestCase(0, 0)]
    [TestCase(0x000000FF, unchecked((int)0xFF000000))]
    [TestCase(0x0000FF00, 0x00FF0000)]
    [TestCase(0x00FF0000, 0x0000FF00)]
    [TestCase(unchecked((int)0xFF000000), 0x000000FF)]
    [TestCase(-1, -1)]
    [TestCase(0x12345678, 0x78563412)]
    [TestCase(int.MinValue, 0x00000080)]
    [TestCase(int.MaxValue, unchecked((int)0xFFFFFF7F))]
    [TestCase(-2, unchecked((int)0xFEFFFFFF))]
    [TestCase(unchecked((int)0x80000001), 0x01000080)]
    public void ConvertEndian_Int32_SwapsBytesAndPreservesTwosComplement(int input, int expected)
    {
        Assert.That(Helper.ConvertEndian(input), Is.EqualTo(expected));
        Assert.That(Helper.ConvertEndian(input), Is.EqualTo(LegacyConvertEndian(input)));
    }

    [TestCase(0x0000, 0x0000)]
    [TestCase(0x00FF, 0xFF00)]
    [TestCase(0xFF00, 0x00FF)]
    [TestCase(0xFFFF, 0xFFFF)]
    [TestCase(0x1234, 0x3412)]
    [TestCase(0x0001, 0x0100)]
    public void ConvertEndian_UInt16_SwapsBytes(int input, int expected)
    {
        ushort value = (ushort)input;
        Assert.That(Helper.ConvertEndian(value), Is.EqualTo((ushort)expected));
        Assert.That(Helper.ConvertEndian(value), Is.EqualTo(LegacyConvertEndian(value)));
    }

    [TestCase(0, 0)]
    [TestCase(0x00FF, unchecked((short)0xFF00))]
    [TestCase(unchecked((short)0xFF00), 0x00FF)]
    [TestCase(-1, -1)]
    [TestCase(0x1234, 0x3412)]
    [TestCase(short.MinValue, 0x0080)]
    [TestCase(short.MaxValue, unchecked((short)0xFF7F))]
    [TestCase(-2, unchecked((short)0xFEFF))]
    [TestCase(unchecked((short)0x8001), unchecked((short)0x0180))]
    public void ConvertEndian_Int16_SwapsBytesAndPreservesTwosComplement(int input, int expected)
    {
        short value = (short)input;
        Assert.That(Helper.ConvertEndian(value), Is.EqualTo((short)expected));
        Assert.That(Helper.ConvertEndian(value), Is.EqualTo(LegacyConvertEndian(value)));
    }

    [Test]
    public void ConvertEndian_16Bit_MatchesLegacyOverEveryValue()
    {
        for (int i = 0; i <= ushort.MaxValue; i++)
        {
            ushort u = (ushort)i;
            short s = (short)i;

            Assert.That(Helper.ConvertEndian(u), Is.EqualTo(LegacyConvertEndian(u)), $"ushort 0x{i:X4}");
            Assert.That(Helper.ConvertEndian(s), Is.EqualTo(LegacyConvertEndian(s)), $"short 0x{i:X4}");
        }
    }

    [Test]
    public void ConvertEndian_32Bit_MatchesLegacyOverPseudoRandomSample()
    {
        var random = new Random(20260901);
        Span<byte> buffer = stackalloc byte[4];
        for (int i = 0; i < 200_000; i++)
        {
            random.NextBytes(buffer);
            uint u = BitConverter.ToUInt32(buffer);
            int s = BitConverter.ToInt32(buffer);

            Assert.That(Helper.ConvertEndian(u), Is.EqualTo(LegacyConvertEndian(u)), $"uint 0x{u:X8}");
            Assert.That(Helper.ConvertEndian(s), Is.EqualTo(LegacyConvertEndian(s)), $"int 0x{s:X8}");
        }
    }

    // Composing the call with itself proves nothing on its own - the identity round-trips too - so each
    // pair names the swapped value the other leg starts from. The call has to land on that exact
    // intermediate, and only then come back.
    [Test]
    public void ConvertEndian_ReachesTheSwappedValueAndReturnsFromIt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Helper.ConvertEndian(0x12345678u), Is.EqualTo(0x78563412u));
            Assert.That(Helper.ConvertEndian(0x78563412u), Is.EqualTo(0x12345678u));

            Assert.That(Helper.ConvertEndian(int.MinValue), Is.EqualTo(0x00000080));
            Assert.That(Helper.ConvertEndian(0x00000080), Is.EqualTo(int.MinValue));

            Assert.That(Helper.ConvertEndian((ushort)0x1234), Is.EqualTo((ushort)0x3412));
            Assert.That(Helper.ConvertEndian((ushort)0x3412), Is.EqualTo((ushort)0x1234));

            Assert.That(Helper.ConvertEndian(short.MinValue), Is.EqualTo((short)0x0080));
            Assert.That(Helper.ConvertEndian((short)0x0080), Is.EqualTo(short.MinValue));
        });
    }

    [Test]
    public void ConvertEndian_IsNotIdentity_ForAsymmetricValues()
    {
        Assert.That(Helper.ConvertEndian(0x12345678u), Is.Not.EqualTo(0x12345678u));
        Assert.That(Helper.ConvertEndian(0x000000FFu), Is.Not.EqualTo(0x000000FFu));
        Assert.That(Helper.ConvertEndian(0x12345678), Is.Not.EqualTo(0x12345678));
        Assert.That(Helper.ConvertEndian(int.MinValue), Is.Not.EqualTo(int.MinValue));
        Assert.That(Helper.ConvertEndian((ushort)0x1234), Is.Not.EqualTo((ushort)0x1234));
        Assert.That(Helper.ConvertEndian(short.MaxValue), Is.Not.EqualTo(short.MaxValue));
    }

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
    // The decoder reads its fields through BitConverter over the raw bytes and then reverses, which is
    // host-endianness dependent and therefore only defined on the little-endian platforms Beutl supports.
    // The ConvertEndian cases above are not: BinaryPrimitives.ReverseEndianness and the legacy oracle both
    // reverse bytes whatever the host does.
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
