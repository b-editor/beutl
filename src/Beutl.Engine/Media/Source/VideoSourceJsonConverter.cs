using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.Media.Source;

public sealed class VideoSourceJsonConverter : JsonConverter<IVideoSource?>
{
    public override IVideoSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        
        return s != null && VideoSource.TryOpen(s, out var videoSource)
            ? videoSource
            : null;
    }

    public override void Write(Utf8JsonWriter writer, IVideoSource? value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value?.Name);
    }
}
