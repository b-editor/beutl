using System.Text.Json;
using System.Text.Json.Serialization;

using BeUtl.Graphics;

namespace BeUtl.Converters;

internal sealed class RelativePointJsonConverter : JsonConverter<RelativePoint>
{
    public override RelativePoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid PixelPoint.");

        return RelativePoint.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, RelativePoint value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
