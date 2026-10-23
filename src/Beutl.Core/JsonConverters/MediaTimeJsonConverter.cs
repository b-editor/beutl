using System.Reactive.Disposables;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Beutl.Serialization;

namespace Beutl.JsonConverters;

// Files hold times as flick counts. A TimeSpan string, as earlier versions wrote and agent documents still use, reads
// rounded up to a flick, and on the frame grid of the project whose files are read.
internal sealed class MediaTimeJsonConverter : JsonConverter<MediaTime>
{
    [ThreadStatic]
    private static int t_stringScopes;

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

    // Agent documents show and take times as TimeSpan strings rounded down to a tick, as earlier versions wrote them.
    // The scope belongs to the calling thread, so it must not span an await.
    internal static IDisposable WriteTimeSpanStrings()
    {
        t_stringScopes++;
        return Disposable.Create(static () => t_stringScopes--);
    }

    // A time less than two ticks from a frame, as a template's or a hand-written time can be, as that frame.
    internal static MediaTime SnapToFrame(MediaTime time, Rational rate)
    {
        return TryRoundToFrame(time, rate, out MediaTime frame)
               && Math.Abs(frame.Flicks - time.Flicks) < MediaTime.FromTicks(2, MidpointRounding.ToPositiveInfinity).Flicks
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

    // A length written as a TimeSpan string is the distance between its ends on the tick grid, so ends that met in ticks
    // still meet.
    internal static MediaTime LengthToWrite(MediaTime start, MediaTime length)
    {
        if (t_stringScopes == 0)
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
        if (t_stringScopes == 0)
        {
            writer.WriteNumberValue(value.Flicks);
            return;
        }

        var ticks = TimeSpan.FromTicks(value.ToTicks(MidpointRounding.ToNegativeInfinity));
        JsonMetadataServices.TimeSpanConverter.Write(writer, ticks, options);
    }
}
