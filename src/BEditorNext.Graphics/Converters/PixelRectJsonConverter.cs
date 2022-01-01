using System.Text.Json;
using System.Text.Json.Serialization;

using BEditorNext.Media;

namespace BEditorNext.Converters;

internal sealed class PixelRectJsonConverter : JsonConverter<PixelRect>
{
    public override PixelRect Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid PixelRect.");

        return PixelRect.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, PixelRect value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
