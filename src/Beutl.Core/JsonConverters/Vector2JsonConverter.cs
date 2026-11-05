using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Beutl.Utilities;

namespace Beutl.JsonConverters;

internal sealed class Vector2JsonConverter : JsonConverter<Vector2>
{
    public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid Vector2.");

        using var tokenizer = new RefStringTokenizer(s, exceptionMessage: "Invalid Vector2.");
        float x = tokenizer.ReadSingle();
        float y = tokenizer.ReadSingle();
        return new Vector2(x, y);
    }

    public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(FormattableString.Invariant($"{value.X},{value.Y}"));
    }
}
