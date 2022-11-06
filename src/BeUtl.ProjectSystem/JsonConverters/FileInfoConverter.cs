using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.JsonConverters;

internal class FileInfoConverter : JsonConverter<FileInfo>
{
    public override FileInfo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid FileInfo.");

        return new FileInfo(s);
    }

    public override void Write(Utf8JsonWriter writer, FileInfo value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.FullName);
    }
}
