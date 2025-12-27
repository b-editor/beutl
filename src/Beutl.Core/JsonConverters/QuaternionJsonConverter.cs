using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.Utilities;

namespace Beutl.JsonConverters;

internal sealed class QuaternionJsonConverter : JsonConverter<Quaternion>
{
    public override Quaternion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid Quaternion.");

        var tokenizer = new RefStringTokenizer(s, exceptionMessage: "Invalid Quaternion.");
        float x = tokenizer.ReadSingle();
        float y = tokenizer.ReadSingle();
        float z = tokenizer.ReadSingle();
        float w = tokenizer.ReadSingle();
        return new Quaternion(x, y, z, w);
    }

    public override void Write(Utf8JsonWriter writer, Quaternion value, JsonSerializerOptions options)
    {
        writer.WriteStringValue($"{value.X},{value.Y},{value.Z},{value.W}");
    }
}
