using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.Graphics;

namespace Beutl.Converters;

internal sealed class RelativeRectJsonConverter : JsonConverter<RelativeRect>
{
    public override RelativeRect Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid RelativeRect.");

        return RelativeRect.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, RelativeRect value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
