using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Beutl.Utilities;

namespace Beutl.JsonConverters;

internal sealed class Vector4JsonConverter : JsonConverter<Vector4>
{
    public override Vector4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid Vector4.");

        using var tokenizer = new RefStringTokenizer(s, exceptionMessage: "Invalid Vector4.");
        float x = tokenizer.ReadSingle();
        float y = tokenizer.ReadSingle();
        float z = tokenizer.ReadSingle();
        float w = tokenizer.ReadSingle();
        return new Vector4(x, y, z, w);
    }

    public override void Write(Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(FormattableString.Invariant($"{value.X},{value.Y},{value.Z},{value.W}"));
    }
}
