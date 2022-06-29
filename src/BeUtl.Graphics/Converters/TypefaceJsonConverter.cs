using System.Text.Json;
using System.Text.Json.Serialization;

using BeUtl.Media;

namespace BeUtl.Converters;

internal sealed class TypefaceJsonConverter : JsonConverter<Typeface>
{
    public override Typeface Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new Exception("Invalid Typeface.");

        string? fontFamily = null;
        FontWeight weight = FontWeight.Regular;
        FontStyle style = FontStyle.Normal;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                if (fontFamily != null)
                {
                    return new Typeface(new FontFamily(fontFamily), style, weight);
                }
                else
                {
                    throw new Exception("Invalid Typeface.");
                }
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new Exception("Invalid Typeface.");
            }

            switch (reader.GetString())
            {
                case "fontfamily" when reader.Read():
                    fontFamily = reader.GetString();
                    break;
                case "weight" when reader.Read():
                    weight = (FontWeight)reader.GetInt32();
                    break;
                case "style" when reader.Read():
                    style = (FontStyle)reader.GetInt32();
                    break;
                default:
                    break;
            }
        }
        throw new Exception("Invalid Typeface.");
    }

    public override void Write(Utf8JsonWriter writer, Typeface value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteString("fontfamily", value.FontFamily.Name);
        writer.WriteNumber("weight", (int)value.Weight);
        writer.WriteNumber("style", (int)value.Style);

        writer.WriteEndObject();
    }
}
