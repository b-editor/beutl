using System.Buffers.Binary;
using System.Text;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Media;

// The faces after the first of a font collection. Skia's CoreText port opens only face 0, so macOS loads a
// later face from a copy of its tables; the copies of one collection are capped so many faces cannot exhaust memory.
internal sealed class FontCollection(string file, byte[]? bytes = null, long maxCopiedBytes = FontCollection.MaxCopiedBytes)
{
    internal const long MaxCopiedBytes = 256L * 1024 * 1024;
    private const uint NameTableTag = 0x6E616D65;
    private static readonly ILogger s_logger = Log.CreateLogger<FontCollection>();
    private SKData? _mapped;
    private long _copiedBytes;
    private bool _capped;

    public string File { get; } = file;

    // The content hash a project font is cached under; null for an installed font.
    public string? Hash { get; init; }

    // Null once the copies of this collection would pass the cap, or when the face is malformed.
    public SKTypeface? LoadCopy(int index)
    {
        if (_capped) return null;
        // An installed file is mapped rather than read, so its pending faces do not hold a copy of it.
        _mapped ??= bytes is null ? SKData.Create(File) : null;
        ReadOnlySpan<byte> source = bytes ?? (_mapped is { } mapped ? mapped.AsSpan() : []);
        if (CopyFace(source, index) is not { } face) return null;
        if (_copiedBytes + face.Length > maxCopiedBytes)
        {
            _capped = true;
            s_logger.LogWarning("Skipped the remaining faces of {File}: their copies would pass {Limit} bytes.", File, maxCopiedBytes);
            return null;
        }

        _copiedBytes += face.Length;
        using var stream = new MemoryStream(face, writable: false);
        return SKTypeface.FromStream(stream);
    }

    // The family of each face after the first, read from its name table without loading the face.
    public static List<(int Index, string Family, FontName? Names)> ReadLaterFamilies(Stream collection, int count)
    {
        var families = new List<(int, string, FontName?)>();
        using var reader = new BinaryReader(collection, System.Text.Encoding.UTF8, leaveOpen: true);
        for (int index = 1; index < count; index++)
        {
            try
            {
                collection.Position = 12 + index * 4;
                collection.Position = ReadUInt32(reader);
                collection.Position += 4;
                int tables = ReadUInt16(reader);
                collection.Position += 6;
                byte[] records = reader.ReadBytes(tables * 16);
                for (int table = 0; table < tables; table++)
                {
                    ReadOnlySpan<byte> record = records.AsSpan(table * 16, 16);
                    if (BinaryPrimitives.ReadUInt32BigEndian(record) != NameTableTag) continue;
                    collection.Position = BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
                    // A name table is a few kilobytes; a larger one is malformed.
                    byte[] name = reader.ReadBytes((int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(record[12..]), ushort.MaxValue * 2));
                    if (ReadFamily(name) is { } family)
                        families.Add((index, family, ReadNames(name)));
                    break;
                }
            }
            catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException)
            {
                // A malformed face is left out.
            }
        }

        return families;
    }

    // CoreText groups a face under its typographic family name, else its legacy one, in English.
    private static string? ReadFamily(ReadOnlySpan<byte> name)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(name[2..]);
        int strings = BinaryPrimitives.ReadUInt16BigEndian(name[4..]);
        foreach (ushort id in (ReadOnlySpan<ushort>)[16, 1])
        {
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> record = name.Slice(6 + i * 12, 12);
                ushort platform = BinaryPrimitives.ReadUInt16BigEndian(record);
                ushort language = BinaryPrimitives.ReadUInt16BigEndian(record[4..]);
                bool macRoman = platform == 1 && BinaryPrimitives.ReadUInt16BigEndian(record[2..]) == 0 && language == 0;
                if (BinaryPrimitives.ReadUInt16BigEndian(record[6..]) != id
                    || !(platform == 0 || macRoman || (platform == 3 && language == 0x409)))
                {
                    continue;
                }

                ReadOnlySpan<byte> value = name.Slice(
                    strings + BinaryPrimitives.ReadUInt16BigEndian(record[10..]),
                    BinaryPrimitives.ReadUInt16BigEndian(record[8..]));
                string text = (macRoman
                        ? CodePagesEncodingProvider.Instance.GetEncoding(10000) ?? System.Text.Encoding.Latin1
                        : System.Text.Encoding.BigEndianUnicode)
                    .GetString(value).Replace("\0", string.Empty);
                if (text.Length > 0) return text;
            }
        }

        return null;
    }

    private static FontName? ReadNames(byte[] name)
    {
        try
        {
            return FontName.ReadFontName(new MemoryStream(name, writable: false));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            return null;
        }
    }

    // A standalone font with one face of a collection, or null when the collection is malformed.
    internal static byte[]? CopyFace(ReadOnlySpan<byte> collection, int index)
    {
        try
        {
            int start = checked((int)BinaryPrimitives.ReadUInt32BigEndian(collection[(12 + index * 4)..]));
            int tables = BinaryPrimitives.ReadUInt16BigEndian(collection[(start + 4)..]);
            byte[] directory = collection.Slice(start, 12 + tables * 16).ToArray();
            using var font = new MemoryStream();
            font.Write(directory);
            for (int table = 0; table < tables; table++)
            {
                Span<byte> record = directory.AsSpan(12 + table * 16, 16);
                int offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]));
                int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]));
                // One face's tables cannot add up to more than the collection holds.
                if (font.Length + length > directory.Length + collection.Length) return null;
                font.Position = (font.Length + 3) & ~3L;
                BinaryPrimitives.WriteUInt32BigEndian(record[8..], (uint)font.Position);
                font.Write(collection.Slice(offset, length));
            }

            font.Position = 0;
            font.Write(directory);
            return font.ToArray();
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    private static uint ReadUInt32(BinaryReader reader) => BinaryPrimitives.ReverseEndianness(reader.ReadUInt32());

    private static ushort ReadUInt16(BinaryReader reader) => BinaryPrimitives.ReverseEndianness(reader.ReadUInt16());
}
