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
    CopyrightNotice = 0,
    FontFamilyName = 1,
    FontSubfamilyName = 2,
    UniqueFontID = 3,
    FullFontName = 4,
    Version = 5,
    PostscriptName = 6,
    Trademark = 7,
    Manufacturer = 8,
    Designer = 9,
    Description = 10,
    VendorUrl = 11,
    DesignerUrl = 12,
    LicenseDescription = 13,
    LicenseInfoUrl = 14,
    TypographicFamilyName = 16,
    TypographicSubfamilyName = 17,
    SampleText = 19,
}

internal record FontName(
    string? CopyrightNotice,
    string? FontFamilyName,
    string? FontSubfamilyName,
    string? UniqueFontID,
    string? FullFontName,
    string? Version,
    string? PostscriptName,
    string? Trademark,
    string? Manufacturer,
    string? Designer,
    string? Description,
    string? VendorUrl,
    string? DesignerUrl,
    string? LicenseDescription,
    string? LicenseInfoUrl,
    string? TypographicFamilyName,
    string? TypographicSubfamilyName,
    string? SampleText)
{
    private static ushort ReadUInt16(BinaryReader reader)
    {
        return BitConverter.ToUInt16(reader.ReadBytes(2).Reverse().ToArray(), 0);
    }

    private static uint ReadUInt32(BinaryReader reader)
    {
        return BitConverter.ToUInt32(reader.ReadBytes(4).Reverse().ToArray(), 0);
    }

    static System.Text.Encoding AsEncoding(EncodingIDs id)
    {
        switch (id)
        {
            case EncodingIDs.Unicode11:
            case EncodingIDs.Unicode2:
                return System.Text.Encoding.BigEndianUnicode;
            default:
                return System.Text.Encoding.UTF8;
        }
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
                var enc = AsEncoding((EncodingIDs)encodingID);
                string nameValue = enc.GetString(nameBytes).Replace("\0", string.Empty);
                reader.BaseStream.Seek(currentPos, SeekOrigin.Begin);

                entry.Add(((PlatformIDs)platformID, languageID, (KnownNameIds)nameID, nameValue));
            }
        }

        return new FontName(
            CopyrightNotice: GetNameById(CultureInfo.CurrentUICulture, KnownNameIds.CopyrightNotice),
            FontFamilyName: GetNameById(CultureInfo.CurrentUICulture, KnownNameIds.FontFamilyName),
            FontSubfamilyName: GetNameById(CultureInfo.CurrentUICulture, KnownNameIds.FontSubfamilyName),
            UniqueFontID: GetNameById(CultureInfo.CurrentUICulture, KnownNameIds.UniqueFontID),
            FullFontName: GetNameById(CultureInfo.CurrentUICulture, KnownNameIds.FullFontName),
            Version: GetNameById(CultureInfo.CurrentUICulture, KnownNameIds.Version),
            PostscriptName: GetNameById(CultureInfo.CurrentUICulture, KnownNameIds.PostscriptName),
            Trademark: GetNameById(CultureInfo.CurrentUICulture, KnownNameIds.Trademark),
            Manufacturer: GetNameById(CultureInfo.CurrentUICulture, KnownNameIds.Manufacturer),
            Designer: GetNameById(CultureInfo.CurrentUICulture, KnownNameIds.Designer),
            Description: GetNameById(CultureInfo.CurrentUICulture, KnownNameIds.Description),
            VendorUrl: GetNameById(CultureInfo.CurrentUICulture, KnownNameIds.VendorUrl),
            DesignerUrl: GetNameById(CultureInfo.CurrentUICulture, KnownNameIds.DesignerUrl),
            LicenseDescription: GetNameById(CultureInfo.CurrentUICulture, KnownNameIds.LicenseDescription),
            LicenseInfoUrl: GetNameById(CultureInfo.CurrentUICulture, KnownNameIds.LicenseInfoUrl),
            TypographicFamilyName: GetNameById(CultureInfo.CurrentUICulture, KnownNameIds.TypographicFamilyName),
            TypographicSubfamilyName: GetNameById(CultureInfo.CurrentUICulture, KnownNameIds.TypographicSubfamilyName),
            SampleText: GetNameById(CultureInfo.CurrentUICulture, KnownNameIds.SampleText)
        );

        string GetNameById(CultureInfo culture, KnownNameIds nameId)
        {
            int languageId = culture.LCID;
            string? usaVersion = null;
            string? firstWindows = null;
            string? first = null;
            foreach (var item in entry)
            {
                if (item.Name == nameId)
                {
                    // Get just the first one, just in case.
                    first ??= item.Value;
                    if (item.Platform == PlatformIDs.Windows)
                    {
                        // If us not found return the first windows one.
                        firstWindows ??= item.Value;
                        if (item.Language == 0x0409)
                        {
                            // Grab the us version as its on next best match.
                            usaVersion ??= item.Value;
                        }

                        if (item.Language == languageId)
                        {
                            // Return the most exact first.
                            return item.Value;
                        }
                    }
                }
            }

            return usaVersion ?? firstWindows ?? first ?? string.Empty;
        }
    }
}
