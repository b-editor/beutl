using System.Text.Json;
using System.Text.Json.Serialization;

using BEditorNext.Graphics;

namespace BEditorNext.JsonConverters;

internal class ThicknessConverter : JsonConverter<Thickness>
{
    public override Thickness Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid Thickness.");

        return Thickness.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, Thickness value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
