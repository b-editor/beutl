using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.Audio;

namespace Beutl.Converters;

internal sealed class AudioChannelMapJsonConverter : JsonConverter<AudioChannelMap>
{
    public override AudioChannelMap Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        try
        {
            JsonElement root = document.RootElement;
            if (!Enum.TryParse(root.GetProperty("Format").GetString(), out AudioChannelFormat format)
                || !Enum.IsDefined(format))
                throw new JsonException("Invalid channel format.");

            int[] sources = root.TryGetProperty("Sources", out JsonElement array)
                ? array.EnumerateArray().Select(value => value.GetInt32()).ToArray() : [];
            AudioChannelLayout layout = default;
            if (format != AudioChannelFormat.Auto)
            {
                JsonElement sourceLayout = root.GetProperty("SourceLayout");
                layout = new AudioChannelLayout(sourceLayout.GetProperty("ChannelCount").GetInt32(),
                    (AudioSpeakers)sourceLayout.GetProperty("Speakers").GetUInt32());
            }
            return new AudioChannelMap(format, layout, sources);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new JsonException("Invalid AudioChannelMap.", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, AudioChannelMap value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Format", value.Format.ToString());
        writer.WriteStartArray("Sources");
        foreach (int source in value.Sources) writer.WriteNumberValue(source);
        writer.WriteEndArray();
        if (value.Format != AudioChannelFormat.Auto)
        {
            writer.WriteStartObject("SourceLayout");
            writer.WriteNumber("ChannelCount", value.SourceLayout.ChannelCount);
            writer.WriteNumber("Speakers", (uint)value.SourceLayout.Speakers);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }
}
