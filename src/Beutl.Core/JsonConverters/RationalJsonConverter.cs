using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.JsonConverters;

internal sealed class RationalJsonConverter : JsonConverter<Rational>
{
    public override Rational Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        return s == null ? throw new Exception("Invalid Rational.") : Rational.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, Rational value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
