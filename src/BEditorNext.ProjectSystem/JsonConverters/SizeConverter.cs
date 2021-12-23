using System.Text.Json;
using System.Text.Json.Serialization;
using BEditorNext.Graphics;

namespace BEditorNext.JsonConverters;

internal class SizeConverter : JsonConverter<Size>
{
    public override Size Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid Size.");

        return Size.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, Size value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
