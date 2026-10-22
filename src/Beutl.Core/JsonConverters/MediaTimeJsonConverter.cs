using System.Reactive.Disposables;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Beutl.Serialization;

namespace Beutl.JsonConverters;

// Files keep the TimeSpan strings of earlier versions until #2885 raises the format floor. A time is written rounded down
// to a tick and read rounded up to a flick, so a saved time never moves later and a file read and saved stays the same.
internal sealed class MediaTimeJsonConverter : JsonConverter<MediaTime>
{
    [ThreadStatic]
    private static int t_flickScopes;

    // Copies that stay in this process (snapshots, duplicates, the clipboard) write exact flicks instead.
    // The scope belongs to the calling thread, so it must not span an await.
    internal static IDisposable WriteFlicks()
    {
        t_flickScopes++;
        return Disposable.Create(static () => t_flickScopes--);
    }

    // A time read from a file falls less than a tick short of its frame; that frame is used, and other times are kept.
    internal static MediaTime SnapToFrame(MediaTime time, Rational rate)
    {
        MediaTime frame = time.RoundToFrame(rate, MidpointRounding.ToPositiveInfinity);
        return frame - time < MediaTime.FromTicks(1, MidpointRounding.ToPositiveInfinity) ? frame : time;
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

        long endTicks = start.ToTicks(MidpointRounding.ToNegativeInfinity) + length.ToTicks(MidpointRounding.ToNegativeInfinity);
        return MediaTime.FromTicks(endTicks, MidpointRounding.ToPositiveInfinity) - start;
    }

    public override MediaTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new MediaTime(reader.GetInt64());

        TimeSpan value = JsonMetadataServices.TimeSpanConverter.Read(ref reader, typeof(TimeSpan), options);
        return MediaTime.FromTicks(value.Ticks, MidpointRounding.ToPositiveInfinity);
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
