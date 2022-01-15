using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeUtl.JsonConverters;

internal class DirectoryInfoConverter : JsonConverter<DirectoryInfo>
{
    public override DirectoryInfo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid DirectoryInfo.");

        return new DirectoryInfo(s);
    }

    public override void Write(Utf8JsonWriter writer, DirectoryInfo value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.FullName);
    }
}
