using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeUtl.JsonConverters;

internal class CultureInfoConverter : JsonConverter<CultureInfo>
{
    public override CultureInfo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid CultureInfo.");

        return CultureInfo.GetCultureInfo(s);
    }

    public override void Write(Utf8JsonWriter writer, CultureInfo value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Name);
    }
}
