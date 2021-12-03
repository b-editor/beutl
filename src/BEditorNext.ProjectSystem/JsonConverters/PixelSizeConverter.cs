using System.Text.Json;
using System.Text.Json.Serialization;

using BEditorNext.Media;

namespace BEditorNext.JsonConverters;

internal class PixelSizeConverter : JsonConverter<PixelSize>
{
    public override PixelSize Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid PixelSize.");

        return PixelSize.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, PixelSize value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
