using System.Globalization;
using System.Text;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class FontNameTests
{
    private const ushort UnicodePlatformId = 0;
    private const ushort MacintoshPlatformId = 1;
    private const ushort WindowsPlatformId = 3;
    private const ushort SymbolEncodingId = 0;
    private const ushort Unicode11EncodingId = 1;
    private const ushort UsEnglishLanguageId = 0x0409;
    private const ushort JapaneseLanguageId = 0x0411;
    private const ushort FontFamilyNameId = 1;
    private const ushort TypographicFamilyNameId = 16;

    public static IEnumerable<TestCaseData> UInt16Patterns()
    {
        yield return new TestCaseData(new byte[] { 0x00, 0x00 }, (ushort)0x0000);
        yield return new TestCaseData(new byte[] { 0x00, 0xFF }, (ushort)0x00FF);
        yield return new TestCaseData(new byte[] { 0xFF, 0x00 }, (ushort)0xFF00);
        yield return new TestCaseData(new byte[] { 0xFF, 0xFF }, (ushort)0xFFFF);
        yield return new TestCaseData(new byte[] { 0x12, 0x34 }, (ushort)0x1234);
    }

    [TestCaseSource(nameof(UInt16Patterns))]
    public void ReadUInt16_ReadsTheBigEndianValue(byte[] bytes, ushort expected)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);

        Assert.That(FontName.ReadUInt16(reader), Is.EqualTo(expected));
    }

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

    [Test]
    public void ReadUInt16_ThrowsWhenTheTableIsTruncated()
    {
        using var stream = new MemoryStream([0x01], writable: false);
        using var reader = new BinaryReader(stream);

        Assert.Throws<EndOfStreamException>(() => FontName.ReadUInt16(reader));
    }

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

    [Test]
    public void GetFamilySpellings_UsesTheTypographicFamilyOverAWeightedLegacyName()
    {
        FontName name = Read(
            Record(FontFamilyNameId, UsEnglishLanguageId, "Inter Thin"),
            Record(TypographicFamilyNameId, UsEnglishLanguageId, "Inter"));

        Assert.That(FontName.Localize(name.GetFamilySpellings("Inter"), CultureInfo.GetCultureInfo("en-US")),
            Is.EqualTo("Inter"));
    }

    [Test]
    public void GetFamilySpellings_UsesTheLegacyNameWhenTheFamilyIsGroupedUnderIt()
    {
        // Optical sizes share a typographic family but are separate families to a font manager.
        FontName name = Read(
            Record(FontFamilyNameId, UsEnglishLanguageId, "Sitka Small"),
            Record(TypographicFamilyNameId, UsEnglishLanguageId, "Sitka"));

        Assert.That(FontName.Localize(name.GetFamilySpellings("Sitka Small"), CultureInfo.GetCultureInfo("en-US")),
            Is.EqualTo("Sitka Small"));
    }

    [Test]
    public void GetFamilySpellings_ReturnsEveryLanguageOfTheMatchingRecord()
    {
        FontName name = Read(
            Record(FontFamilyNameId, UsEnglishLanguageId, "Yu Gothic Light"),
            Record(FontFamilyNameId, JapaneseLanguageId, "游ゴシック Light"),
            Record(TypographicFamilyNameId, UsEnglishLanguageId, "Yu Gothic"),
            Record(TypographicFamilyNameId, JapaneseLanguageId, "游ゴシック"));

        Assert.That(name.GetFamilySpellings("yu gothic").Select(r => r.Value),
            Is.EquivalentTo(new[] { "Yu Gothic", "游ゴシック" }));
    }

    [Test]
    public void GetFamilySpellings_MatchesTheFamilyByAnyOfItsLanguages()
    {
        FontName name = Read(
            Record(TypographicFamilyNameId, UsEnglishLanguageId, "Yu Gothic"),
            Record(TypographicFamilyNameId, JapaneseLanguageId, "游ゴシック"));

        Assert.That(FontName.Localize(name.GetFamilySpellings("游ゴシック"), CultureInfo.GetCultureInfo("en-US")),
            Is.EqualTo("Yu Gothic"));
    }

    [Test]
    public void GetFamilySpellings_IsEmptyForAFamilyTheTableDoesNotName()
    {
        FontName name = Read(Record(FontFamilyNameId, UsEnglishLanguageId, "Inter Thin"));

        Assert.That(name.GetFamilySpellings("Inter"), Is.Empty);
    }

    [TestCase("ja-JP", "游ゴシック")]
    [TestCase("en-US", "Yu Gothic")]
    [TestCase("es-ES", "Yu Gothic")]
    public void Localize_PrefersTheCultureThenUsEnglish(string culture, string expected)
    {
        FontName name = Read(
            Record(TypographicFamilyNameId, JapaneseLanguageId, "游ゴシック"),
            Record(TypographicFamilyNameId, UsEnglishLanguageId, "Yu Gothic"));

        Assert.That(FontName.Localize(name.GetFamilySpellings("Yu Gothic"), CultureInfo.GetCultureInfo(culture)),
            Is.EqualTo(expected));
    }

    [Test]
    public void ReadFontName_DecodesWindowsNamesAsUtf16WhateverTheEncodingId()
    {
        // Symbol fonts mark their Windows names with encoding 0; the strings are UTF-16BE all the same.
        FontName name = Read(Record(FontFamilyNameId, UsEnglishLanguageId, "記号 Symbol", encodingId: SymbolEncodingId));

        Assert.That(name.FamilyNames.Select(r => r.Value), Is.EqualTo(new[] { "記号 Symbol" }));
    }

    [Test]
    public void ReadFontName_KeepsOnlyUnicodeAndWindowsFamilyNames()
    {
        FontName name = Read(
            Record(FontFamilyNameId, 0, "Unicode Name", platformId: UnicodePlatformId),
            Record(FontFamilyNameId, 0, "Mac Name", platformId: MacintoshPlatformId),
            Record(FontFamilyNameId, UsEnglishLanguageId, "Windows Name"),
            Record(4, UsEnglishLanguageId, "Windows Name Bold"));

        Assert.That(name.FamilyNames.Select(r => r.Value), Is.EqualTo(new[] { "Unicode Name", "Windows Name" }));
    }

    private static FontName Read(params NameRecord[] records)
    {
        using var stream = new MemoryStream(BuildNameTable(records), writable: false);
        return FontName.ReadFontName(stream);
    }

    private static NameRecord Record(
        ushort nameId, ushort languageId, string value,
        ushort platformId = WindowsPlatformId, ushort encodingId = Unicode11EncodingId)
        => new(platformId, encodingId, languageId, nameId, value);

    private readonly record struct NameRecord(
        ushort PlatformId, ushort EncodingId, ushort LanguageId, ushort NameId, string Value);

    private static byte[] BuildNameTable(params (ushort LanguageId, string Value)[] records)
    {
        return BuildNameTable(records
            .Select(record => Record(FontFamilyNameId, record.LanguageId, record.Value))
            .ToArray());
    }

    private static byte[] BuildNameTable(params NameRecord[] records)
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
            WriteBigEndian(writer, records[index].PlatformId);
            WriteBigEndian(writer, records[index].EncodingId);
            WriteBigEndian(writer, records[index].LanguageId);
            WriteBigEndian(writer, records[index].NameId);
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
