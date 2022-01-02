using System.Text.Json;
using System.Text.Json.Serialization;

using BEditorNext.Graphics;

namespace BEditorNext.Converters;

internal sealed class VectorJsonConverter : JsonConverter<Vector>
{
    public override Vector Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid Point.");

        return Vector.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, Vector value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
