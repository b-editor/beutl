using System.Buffers.Binary;
using System.Globalization;

namespace Beutl.Media;

internal enum EncodingIDs : ushort
{
    Unicode1 = 0,
    Unicode11 = 1,
    ISO10646 = 2,
    Unicode2 = 3,
    Unicode2Plus = 4,
    UnicodeVariationSequences = 5,
    UnicodeFull = 6,
}

internal enum PlatformIDs : ushort
{
    Unicode = 0,
    Macintosh = 1,
    ISO = 2,
    Windows = 3,
    Custom = 4 // Custom  None
}

internal enum KnownNameIds : ushort
{
    FontFamilyName = 1,
    TypographicFamilyName = 16,
    WwsFamilyName = 21,
}

// A family name record as the name table spells it for one platform and language.
internal readonly record struct FamilyNameRecord(KnownNameIds NameId, PlatformIDs Platform, ushort LanguageId, string Value);

internal record FontName
{
    private const ushort UsEnglishLanguageId = 0x0409;

    // A font manager groups faces under one of these names: the typographic family, the WWS family
    // or the legacy family, which may carry a weight such as "Inter Thin".
    private static readonly KnownNameIds[] s_familyNameIds =
        [KnownNameIds.TypographicFamilyName, KnownNameIds.WwsFamilyName, KnownNameIds.FontFamilyName];

    // The family name records in every language the table carries, Unicode and Windows platforms only.
    public IReadOnlyList<FamilyNameRecord> FamilyNames { get; init; } = [];

    // sfnt tables store big-endian integers; BinaryReader reads little-endian on every platform.
    internal static ushort ReadUInt16(BinaryReader reader)
        => BinaryPrimitives.ReverseEndianness(reader.ReadUInt16());

    static System.Text.Encoding AsEncoding(PlatformIDs platform, EncodingIDs id)
    {
        if (platform == PlatformIDs.Windows && (ushort)id is >= 3 and <= 5)
        {
            // The PRC, Big5 and Wansung Windows encodings store names in code pages 936, 950 and 949.
            int codePage = (ushort)id switch { 3 => 936, 4 => 950, _ => 949 };
            return System.Text.CodePagesEncodingProvider.Instance.GetEncoding(codePage) ?? System.Text.Encoding.UTF8;
        }

        // Every other Windows name, and every Unicode platform name, is UTF-16BE.
        if (platform is PlatformIDs.Unicode or PlatformIDs.Windows)
            return System.Text.Encoding.BigEndianUnicode;

        switch (id)
        {
            case EncodingIDs.Unicode11:
            case EncodingIDs.Unicode2:
                return System.Text.Encoding.BigEndianUnicode;
            default:
                return System.Text.Encoding.UTF8;
        }
    }

    // The spellings of whichever family record names familyName in some language, checking the
    // typographic, WWS and legacy families in that order; empty when none does.
    public IReadOnlyList<FamilyNameRecord> GetFamilySpellings(string familyName)
    {
        foreach (KnownNameIds nameId in s_familyNameIds)
        {
            if (FamilyNames.Any(r => r.NameId == nameId
                && string.Equals(r.Value, familyName, StringComparison.OrdinalIgnoreCase)))
            {
                return [.. FamilyNames.Where(r => r.NameId == nameId)];
            }
        }

        return [];
    }

    // The spelling for the culture: its own Windows record, then US English. Null when the table has
    // neither, since its other spellings can be in any language: Al Nile spells its family in Arabic
    // on Windows and in English only on the Unicode platform, which carries no language.
    public static string? Localize(IReadOnlyList<FamilyNameRecord> spellings, CultureInfo culture)
    {
        string? usEnglish = null;
        foreach (FamilyNameRecord record in spellings)
        {
            if (record.Platform != PlatformIDs.Windows) continue;
            if (record.LanguageId == culture.LCID) return record.Value;
            if (record.LanguageId == UsEnglishLanguageId) usEnglish ??= record.Value;
        }

        return usEnglish;
    }

    public static FontName ReadFontName(Stream stream)
    {
        var entry = new List<(PlatformIDs Platform, ushort Language, KnownNameIds Name, string Value)>();

        using (var reader = new BinaryReader(stream))
        {
            ushort format = ReadUInt16(reader);
            ushort count = ReadUInt16(reader);
            ushort stringOffset = ReadUInt16(reader);

            for (int i = 0; i < count; i++)
            {
                ushort platformID = ReadUInt16(reader);
                ushort encodingID = ReadUInt16(reader);
                ushort languageID = ReadUInt16(reader);
                ushort nameID = ReadUInt16(reader);
                ushort length = ReadUInt16(reader);
                ushort offset = ReadUInt16(reader);

                long currentPos = reader.BaseStream.Position;
                reader.BaseStream.Seek(stringOffset + offset, SeekOrigin.Begin);
                byte[] nameBytes = reader.ReadBytes(length);
                var enc = AsEncoding((PlatformIDs)platformID, (EncodingIDs)encodingID);
                string nameValue = enc.GetString(nameBytes).Replace("\0", string.Empty);
                reader.BaseStream.Seek(currentPos, SeekOrigin.Begin);

                entry.Add(((PlatformIDs)platformID, languageID, (KnownNameIds)nameID, nameValue));
            }
        }

        return new FontName
        {
            FamilyNames =
            [
                .. entry
                    .Where(item => item.Platform is PlatformIDs.Unicode or PlatformIDs.Windows
                        && s_familyNameIds.Contains(item.Name)
                        && item.Value.Length > 0)
                    .Select(item => new FamilyNameRecord(item.Name, item.Platform, item.Language, item.Value))
            ],
        };
    }
}
