using System.Reactive.Disposables;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Beutl.Serialization;

namespace Beutl.JsonConverters;

// Files keep the TimeSpan strings of earlier versions until #2885 raises the format floor. A time is written rounded down
// to a tick and read rounded up to a flick; a project's files are read on its frame grid, so a frame reads back exactly.
internal sealed class MediaTimeJsonConverter : JsonConverter<MediaTime>
{
    [ThreadStatic]
    private static int t_flickScopes;

    // The frame rate of the project whose files are read. Async-local, as a scene reads its element files in parallel.
    private static readonly AsyncLocal<Rational?> s_frameGrid = new();

    internal static bool ReadsOnFrameGrid => s_frameGrid.Value is not null;

    // While a project's files are read, a time less than two ticks from one of its frames reads as that frame.
    internal static IDisposable ReadOnFrameGrid(Rational rate)
    {
        Rational? previous = s_frameGrid.Value;
        s_frameGrid.Value = rate;
        return Disposable.Create(() => s_frameGrid.Value = previous);
    }

    // Ticks from a file read as the frame of `rate` less than two ticks from them, or else rounded up to a flick.
    internal static MediaTime FromFileTicks(long ticks, Rational? rate)
        => rate is { } grid && TryGetFrameNearTicks(ticks, grid, out MediaTime frame)
            ? frame
            : MediaTime.FromTicks(ticks, MidpointRounding.ToPositiveInfinity);

    // The frame of `rate` less than two ticks from the ticks, as a time from a file or typed as TimeSpan text snaps to.
    internal static bool TryGetFrameNearTicks(long ticks, Rational rate, out MediaTime frame)
    {
        if (!TryRoundToFrame(MediaTime.FromTicks(ticks, MidpointRounding.ToPositiveInfinity), rate, out frame))
            return false;

        long below = frame.ToTicks(MidpointRounding.ToNegativeInfinity);
        long above = frame.ToTicks(MidpointRounding.ToPositiveInfinity);
        return below == above
            ? Math.Abs(ticks - below) <= 1
            : ticks >= below - 1 && ticks <= above + 1;
    }

    // Copies that stay in this process (snapshots, duplicates, the clipboard) write exact flicks instead.
    // The scope belongs to the calling thread, so it must not span an await.
    internal static IDisposable WriteFlicks()
    {
        t_flickScopes++;
        return Disposable.Create(static () => t_flickScopes--);
    }

    // A time within a tick of a frame, as a template's or a hand-written time can be, as that frame.
    internal static MediaTime SnapToFrame(MediaTime time, Rational rate)
    {
        return TryRoundToFrame(time, rate, out MediaTime frame)
               && Math.Abs(frame.Flicks - time.Flicks) <= MediaTime.FromTicks(1, MidpointRounding.ToPositiveInfinity).Flicks
            ? frame
            : time;
    }

    // A time near the ends of the range of MediaTime has no frame within it.
    private static bool TryRoundToFrame(MediaTime time, Rational rate, out MediaTime frame)
    {
        try
        {
            frame = time.RoundToFrame(rate, MidpointRounding.AwayFromZero);
            return true;
        }
        catch (OverflowException)
        {
            frame = time;
            return false;
        }
    }

    // A length is written as the distance between its ends on the tick grid, so ends that met in ticks still meet.
    internal static MediaTime LengthToWrite(MediaTime start, MediaTime length)
    {
        if (t_flickScopes > 0)
            return length;

        long ticks = (start + length).ToTicks(MidpointRounding.ToNegativeInfinity)
                     - start.ToTicks(MidpointRounding.ToNegativeInfinity);
        return MediaTime.FromTicks(ticks, MidpointRounding.ToPositiveInfinity);
    }

    // Reverses LengthToWrite for a start and a length that were read from TimeSpan strings.
    internal static MediaTime LengthRead(
        ICoreSerializationContext context, string startName, string lengthName, MediaTime start, MediaTime length)
    {
        if (context is not IJsonSerializationContext json
            || json.GetNode(startName) is not JsonValue startNode || startNode.GetValueKind() != JsonValueKind.String
            || json.GetNode(lengthName) is not JsonValue lengthNode || lengthNode.GetValueKind() != JsonValueKind.String)
        {
            return length;
        }

        long endTicks = startNode.Deserialize<TimeSpan>().Ticks + lengthNode.Deserialize<TimeSpan>().Ticks;
        return FromFileTicks(endTicks, s_frameGrid.Value) - start;
    }

    public override MediaTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new MediaTime(reader.GetInt64());

        TimeSpan value = JsonMetadataServices.TimeSpanConverter.Read(ref reader, typeof(TimeSpan), options);
        return FromFileTicks(value.Ticks, s_frameGrid.Value);
    }

    public override void Write(Utf8JsonWriter writer, MediaTime value, JsonSerializerOptions options)
    {
        if (t_flickScopes > 0)
        {
            writer.WriteNumberValue(value.Flicks);
            return;
        }

        var ticks = TimeSpan.FromTicks(value.ToTicks(MidpointRounding.ToNegativeInfinity));
        JsonMetadataServices.TimeSpanConverter.Write(writer, ticks, options);
    }
}
