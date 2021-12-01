using System.Text.Json;
using System.Text.Json.Serialization;
using BEditorNext.Graphics;

namespace BEditorNext.JsonConverters;

internal class PointConverter : JsonConverter<Point>
{
    public override Point Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid Point.");

        return Point.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, Point value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
