using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.Graphics;

namespace Beutl.Converters;

internal sealed class RelativeRectJsonConverter : JsonConverter<RelativeRect>
{
    public override RelativeRect Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // 互換性のため
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var jsonNode = JsonNode.Parse(ref reader);
            if (jsonNode is JsonObject jsonObject)
            {
                int? unit = (int?)jsonObject["Unit"];
                string? rect = (string?)jsonObject["Rect"];
                if (unit.HasValue && rect != null)
                {
                    return new RelativeRect(Rect.Parse(rect), (RelativeUnit)unit.Value);
                }
            }
            else
            {
                throw new Exception("Invalid RelativeRect.");
            }
        }

        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid RelativeRect.");

        return RelativeRect.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, RelativeRect value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
