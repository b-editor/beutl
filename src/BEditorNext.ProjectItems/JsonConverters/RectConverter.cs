using System.Text.Json;
using System.Text.Json.Serialization;
using BEditorNext.Graphics;

namespace BEditorNext.JsonConverters;

internal class RectConverter : JsonConverter<Rect>
{
    public override Rect Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid Rect.");

        return Rect.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, Rect value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
