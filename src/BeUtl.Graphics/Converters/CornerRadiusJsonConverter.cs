using System.Text.Json;
using System.Text.Json.Serialization;

using BeUtl.Media;

namespace BeUtl.Converters;

internal sealed class CornerRadiusJsonConverter : JsonConverter<CornerRadius>
{
    public override CornerRadius Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid CornerRadius.");

        return CornerRadius.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, CornerRadius value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
