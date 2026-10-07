using System.Buffers.Binary;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

internal static class MFVideoTimeOrigin
{
    // MF reports MP4 video composition timestamps before the edit-list trim.
    // A normal H.264 file can therefore have a nonzero video first-gap even
    // though its audio already starts at zero. Only add the untrimmed gap to
    // NAudio's seek position. This reads metadata, never the media payload.
    public static long GetAudioStartTime(string file, long firstVideoTimestamp)
    {
        if (firstVideoTimestamp <= 0)
            return 0;

        try
        {
            using var stream = File.OpenRead(file);
            return GetAudioStartTime(stream, firstVideoTimestamp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Unavailable metadata must not turn a working, aligned file into
            // an unconditional first-video-PTS audio correction.
            return 0;
        }
    }

    internal static long GetAudioStartTime(Stream stream, long firstVideoTimestamp)
    {
        if (firstVideoTimestamp <= 0 || !stream.CanSeek || stream.Length < 8)
            return 0;

        FourCC firstType = (FourCC)ReadUInt32(stream, 4, stream.Length);
        if (firstType is not (FourCC.Ftyp or FourCC.Moov or FourCC.Mdat or FourCC.Wide or FourCC.Free or FourCC.Skip or FourCC.Uuid))
            return firstVideoTimestamp; // Not an ISO BMFF / QuickTime container.

        try
        {
            Box moov = FindBox(stream, 0, stream.Length, FourCC.Moov) ?? throw new InvalidDataException();
            var video = FindSingleVideoTrack(stream, moov);
            if (video is not { } selected)
                return 0;
            var (track, mdia) = selected;

            Box? edits = FindBox(stream, track.DataStart, track.End, FourCC.Edts);
            Box? list = edits is { } edts ? FindBox(stream, edts.DataStart, edts.End, FourCC.Elst) : null;
            if (list is not { } elst)
                return firstVideoTimestamp;

            Box mdhd = FindBox(stream, mdia.DataStart, mdia.End, FourCC.Mdhd) ?? throw new InvalidDataException();
            uint headerVersion = ReadUInt32(stream, mdhd.DataStart, mdhd.End) >> 24;
            if (headerVersion > 1)
                return 0;
            uint scale = ReadUInt32(stream, mdhd.DataStart + (headerVersion == 1 ? 20 : 12), mdhd.End);
            if (scale == 0)
                return 0;

            uint version = ReadUInt32(stream, elst.DataStart, elst.End) >> 24;
            if (version > 1)
                return 0;
            uint count = ReadUInt32(stream, elst.DataStart + 4, elst.End);
            int entrySize = version == 1 ? 20 : 12;
            long entriesStart = elst.DataStart + 8;
            if (count == 0 || count > (elst.End - entriesStart) / entrySize)
                return 0;
            long? mediaStart = null;
            for (uint i = 0; i < count; i++)
            {
                long position = entriesStart + i * (long)entrySize;
                long time = version == 1
                    ? unchecked((long)ReadUInt64(stream, position + 8, elst.End))
                    : unchecked((int)ReadUInt32(stream, position + 4, elst.End));
                if (ReadUInt32(stream, position + entrySize - 4, elst.End) != 0x00010000)
                    return 0; // Dwell/rate edits need more than a constant seek offset.
                if (time == -1)
                {
                    if (mediaStart.HasValue)
                        return 0; // A trailing gap is not a constant start offset.
                    continue; // Only leading empty edits can be ignored.
                }
                if (time < 0 || mediaStart.HasValue)
                    return 0; // Multiple media segments cannot be mapped by one offset.
                mediaStart = time;
            }

            if (mediaStart is not { } start)
                return 0;
            long trim = checked((long)((Int128)start * TimeSpan.TicksPerSecond / scale));
            return Math.Max(0, firstVideoTimestamp - trim);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OverflowException)
        {
            // Validate every box against its parent; corrupt or unsupported
            // metadata leaves the existing audio time origin in place.
        }
        return 0;
    }

    private enum FourCC : uint
    {
        Ftyp = 0x66747970,
        Moov = 0x6D6F6F76,
        Mdat = 0x6D646174,
        Wide = 0x77696465,
        Free = 0x66726565,
        Skip = 0x736B6970,
        Uuid = 0x75756964,
        Trak = 0x7472616B,
        Tkhd = 0x746B6864,
        Mdia = 0x6D646961,
        Hdlr = 0x68646C72,
        Vide = 0x76696465,
        Edts = 0x65647473,
        Elst = 0x656C7374,
        Mdhd = 0x6D646864,
    }

    private readonly record struct Box(FourCC Type, long DataStart, long End);

    private static (Box Track, Box Media)? FindSingleVideoTrack(Stream stream, Box moov)
    {
        (Box Track, Box Media)? result = null;
        foreach (Box track in Boxes(stream, moov.DataStart, moov.End))
        {
            if (track.Type != FourCC.Trak)
                continue;
            Box? header = FindBox(stream, track.DataStart, track.End, FourCC.Tkhd);
            if (header is { } tkhd && (ReadUInt32(stream, tkhd.DataStart, tkhd.End) & 1) == 0)
                continue;
            Box? media = FindBox(stream, track.DataStart, track.End, FourCC.Mdia);
            if (media is not { } mdia)
                continue;
            Box? handler = FindBox(stream, mdia.DataStart, mdia.End, FourCC.Hdlr);
            if (handler is not { } hdlr || (FourCC)ReadUInt32(stream, hdlr.DataStart + 8, hdlr.End) != FourCC.Vide)
                continue;
            // Without a native stream-to-track identity, multiple enabled video
            // tracks could supply a timestamp and trim from different streams.
            if (result.HasValue)
                return null;
            result = (track, mdia);
        }
        return result;
    }

    private static Box? FindBox(Stream stream, long start, long end, FourCC type)
    {
        foreach (Box box in Boxes(stream, start, end))
            if (box.Type == type)
                return box;
        return null;
    }

    private static IEnumerable<Box> Boxes(Stream stream, long start, long end)
    {
        while (start < end)
        {
            ulong size = ReadUInt32(stream, start, end);
            FourCC type = (FourCC)ReadUInt32(stream, start + 4, end);
            int headerSize = 8;
            if (size == 1)
            {
                size = ReadUInt64(stream, start + 8, end);
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = (ulong)(end - start);
            }
            if (size < (ulong)headerSize || size > (ulong)(end - start))
                throw new InvalidDataException("An MP4 box extends outside its parent.");
            long boxEnd = start + (long)size;
            yield return new Box(type, start + headerSize, boxEnd);
            start = boxEnd;
        }
    }

    private static uint ReadUInt32(Stream stream, long position, long end)
    {
        Span<byte> data = stackalloc byte[4];
        Read(stream, position, end, data);
        return BinaryPrimitives.ReadUInt32BigEndian(data);
    }

    private static ulong ReadUInt64(Stream stream, long position, long end)
    {
        Span<byte> data = stackalloc byte[8];
        Read(stream, position, end, data);
        return BinaryPrimitives.ReadUInt64BigEndian(data);
    }

    private static void Read(Stream stream, long position, long end, Span<byte> data)
    {
        if (position < 0 || position > end - data.Length)
            throw new InvalidDataException("Truncated MP4 metadata.");
        stream.Position = position;
        stream.ReadExactly(data);
    }
}
