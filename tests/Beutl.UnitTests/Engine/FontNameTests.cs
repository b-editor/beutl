using System.Text;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class FontNameTests
{
    private const ushort WindowsPlatformId = 3;
    private const ushort Unicode11EncodingId = 1;
    private const ushort UsEnglishLanguageId = 0x0409;
    private const ushort FontFamilyNameId = 1;

    public static IEnumerable<TestCaseData> UInt16Patterns()
    {
        yield return new TestCaseData(new byte[] { 0x00, 0x00 }, (ushort)0x0000);
        yield return new TestCaseData(new byte[] { 0x00, 0xFF }, (ushort)0x00FF);
        yield return new TestCaseData(new byte[] { 0xFF, 0x00 }, (ushort)0xFF00);
        yield return new TestCaseData(new byte[] { 0xFF, 0xFF }, (ushort)0xFFFF);
        yield return new TestCaseData(new byte[] { 0x12, 0x34 }, (ushort)0x1234);
    }

    public static IEnumerable<TestCaseData> UInt32Patterns()
    {
        yield return new TestCaseData(new byte[] { 0x00, 0x00, 0x00, 0x00 }, 0x00000000u);
        yield return new TestCaseData(new byte[] { 0x00, 0x00, 0x00, 0xFF }, 0x000000FFu);
        yield return new TestCaseData(new byte[] { 0x00, 0x00, 0xFF, 0x00 }, 0x0000FF00u);
        yield return new TestCaseData(new byte[] { 0x00, 0xFF, 0x00, 0x00 }, 0x00FF0000u);
        yield return new TestCaseData(new byte[] { 0xFF, 0x00, 0x00, 0x00 }, 0xFF000000u);
        yield return new TestCaseData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, 0xFFFFFFFFu);
        yield return new TestCaseData(new byte[] { 0x12, 0x34, 0x56, 0x78 }, 0x12345678u);
    }

    [TestCaseSource(nameof(UInt16Patterns))]
    public void ReadUInt16_ReadsTheBigEndianValue(byte[] bytes, ushort expected)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);

        Assert.That(FontName.ReadUInt16(reader), Is.EqualTo(expected));
    }

    [TestCaseSource(nameof(UInt32Patterns))]
    public void ReadUInt32_ReadsTheBigEndianValue(byte[] bytes, uint expected)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);

        Assert.That(FontName.ReadUInt32(reader), Is.EqualTo(expected));
    }

    /// <remarks>
    /// The reads previously went through <c>BitConverter</c> over a reversed copy of the bytes, which is
    /// host-endianness dependent and therefore only defined on the little-endian platforms Beutl supports.
    /// </remarks>
    [TestCaseSource(nameof(UInt16Patterns))]
    public void ReadUInt16_AgreesWithTheReversedBitConverterItReplaced(byte[] bytes, ushort expected)
    {
        Assume.That(BitConverter.IsLittleEndian);

        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);

        ushort legacy = BitConverter.ToUInt16(bytes.Reverse().ToArray(), 0);
        Assert.Multiple(() =>
        {
            Assert.That(legacy, Is.EqualTo(expected));
            Assert.That(FontName.ReadUInt16(reader), Is.EqualTo(legacy));
        });
    }

    /// <inheritdoc cref="ReadUInt16_AgreesWithTheReversedBitConverterItReplaced" />
    [TestCaseSource(nameof(UInt32Patterns))]
    public void ReadUInt32_AgreesWithTheReversedBitConverterItReplaced(byte[] bytes, uint expected)
    {
        Assume.That(BitConverter.IsLittleEndian);

        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);

        uint legacy = BitConverter.ToUInt32(bytes.Reverse().ToArray(), 0);
        Assert.Multiple(() =>
        {
            Assert.That(legacy, Is.EqualTo(expected));
            Assert.That(FontName.ReadUInt32(reader), Is.EqualTo(legacy));
        });
    }

    [Test]
    public void ReadUInt16_ThrowsWhenTheTableIsTruncated()
    {
        using var stream = new MemoryStream([0x01], writable: false);
        using var reader = new BinaryReader(stream);

        Assert.Throws<EndOfStreamException>(() => FontName.ReadUInt16(reader));
    }

    /// <remarks>
    /// Every field this asserts on is a big-endian uint16 whose byte-swapped reading selects a different
    /// record or a different string, so the end-to-end parse fails if any read loses its byte order.
    /// </remarks>
    [Test]
    public void ReadFontName_SelectsTheUsEnglishWindowsRecord()
    {
        byte[] table = BuildNameTable(
            (LanguageId: (ushort)0xFFFF, Value: "ZZ"),
            (LanguageId: UsEnglishLanguageId, Value: "AB"));

        using var stream = new MemoryStream(table, writable: false);
        FontName name = FontName.ReadFontName(stream);

        Assert.That(name.FontFamilyName, Is.EqualTo("AB"));
    }

    [Test]
    public void ReadFontName_ReturnsEmptyForAnAbsentNameId()
    {
        byte[] table = BuildNameTable((LanguageId: UsEnglishLanguageId, Value: "AB"));

        using var stream = new MemoryStream(table, writable: false);
        FontName name = FontName.ReadFontName(stream);

        Assert.That(name.SampleText, Is.Empty);
    }

    private static byte[] BuildNameTable(params (ushort LanguageId, string Value)[] records)
    {
        const int HeaderLength = 6;
        const int RecordLength = 12;
        int stringOffset = HeaderLength + (RecordLength * records.Length);

        byte[][] values = [.. records.Select(record => Encoding.BigEndianUnicode.GetBytes(record.Value))];

        var buffer = new MemoryStream();
        var writer = new BinaryWriter(buffer);
        WriteBigEndian(writer, 0);
        WriteBigEndian(writer, (ushort)records.Length);
        WriteBigEndian(writer, (ushort)stringOffset);

        ushort valueOffset = 0;
        for (int index = 0; index < records.Length; index++)
        {
            WriteBigEndian(writer, WindowsPlatformId);
            WriteBigEndian(writer, Unicode11EncodingId);
            WriteBigEndian(writer, records[index].LanguageId);
            WriteBigEndian(writer, FontFamilyNameId);
            WriteBigEndian(writer, (ushort)values[index].Length);
            WriteBigEndian(writer, valueOffset);
            valueOffset += (ushort)values[index].Length;
        }

        foreach (byte[] value in values)
            writer.Write(value);

        writer.Flush();
        return buffer.ToArray();
    }

    private static void WriteBigEndian(BinaryWriter writer, ushort value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)(value & 0xFF));
    }
}
