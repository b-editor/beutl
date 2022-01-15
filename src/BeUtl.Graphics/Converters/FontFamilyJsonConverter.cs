using System.Text.Json;
using System.Text.Json.Serialization;

using BeUtl.Media;

namespace BeUtl.Converters;

internal sealed class FontFamilyJsonConverter : JsonConverter<FontFamily>
{
    public override FontFamily Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid FontFamily.");

        return new FontFamily(s);
    }

    public override void Write(Utf8JsonWriter writer, FontFamily value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Name);
    }
}
