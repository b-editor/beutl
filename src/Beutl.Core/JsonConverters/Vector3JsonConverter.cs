using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Beutl.Utilities;

namespace Beutl.JsonConverters;

internal sealed class Vector3JsonConverter : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid Vector3.");

        var tokenizer = new RefStringTokenizer(s, exceptionMessage: "Invalid Quaternion.");
        float x = tokenizer.ReadSingle();
        float y = tokenizer.ReadSingle();
        float z = tokenizer.ReadSingle();
        return new Vector3(x, y, z);
    }

    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteStringValue($"{value.X},{value.Y},{value.Z}");
    }
}
