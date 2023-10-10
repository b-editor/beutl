using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.Media.Source;

public sealed class SoundSourceJsonConverter : JsonConverter<ISoundSource?>
{
    public override ISoundSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        
        return s != null && SoundSource.TryOpen(s, out var soundSource)
            ? soundSource
            : null;
    }

    public override void Write(Utf8JsonWriter writer, ISoundSource? value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value?.Name);
    }
}
