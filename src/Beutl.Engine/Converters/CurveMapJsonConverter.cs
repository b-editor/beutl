using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Beutl.Graphics;

namespace Beutl.Converters;

internal sealed class CurveMapJsonConverter : JsonConverter<CurveMap>
{
    public override CurveMap Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Invalid CurveMap.");

        var jsonNode = JsonNode.Parse(ref reader);
        if (jsonNode is not JsonArray jsonArray)
            throw new JsonException("Invalid CurveMap.");

        var points = new List<Point>();
        foreach (JsonNode? item in jsonArray)
        {
            if (item is JsonValue jsonValue && jsonValue.TryGetValue(out string? pointString))
            {
                points.Add(Point.Parse(pointString));
            }
            else
            {
                throw new JsonException("Invalid Point in CurveMap.");
            }
        }

        return new CurveMap(points);
    }

    public override void Write(Utf8JsonWriter writer, CurveMap value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (Point point in value.Points)
        {
            writer.WriteStringValue(point.ToString());
        }
        writer.WriteEndArray();
    }
}
