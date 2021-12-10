using System.Text.Json;
using System.Text.Json.Serialization;

using BEditorNext.Media;

namespace BEditorNext.JsonConverters;

internal class ColorConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid Color.");

        return Color.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
