using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Beutl.JsonConverters;

internal sealed class RationalJsonConverter : JsonConverter<Rational>
{
    public override Rational Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var node = JsonNode.Parse(ref reader) as JsonObject;
            long num = (long)node![nameof(Rational.Numerator)]!;
            long den = (long)node![nameof(Rational.Denominator)]!;
            return new Rational(num, den);
        }
        else
        {
            string? s = reader.GetString();
            return s == null ? throw new Exception("Invalid Rational.") : Rational.Parse(s);
        }
    }

    public override void Write(Utf8JsonWriter writer, Rational value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
