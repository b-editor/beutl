using System.Text.Json;
using System.Text.Json.Serialization;

using Beutl.Graphics;

namespace Beutl.Converters;

internal sealed class MatrixJsonConverter : JsonConverter<Matrix>
{
    public override Matrix Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid Point.");

        return Matrix.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, Matrix value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
