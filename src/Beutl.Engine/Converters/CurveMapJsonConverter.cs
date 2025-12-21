using System.Text.Json;
using System.Text.Json.Serialization;

using Beutl.Graphics;

namespace Beutl.Converters;

internal sealed class CurveMapJsonConverter : JsonConverter<CurveMap>
{
    private static readonly CurveControlPointJsonConverter s_pointConverter = new();

    public override CurveMap Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Invalid CurveMap.");

        var points = new List<CurveControlPoint>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                break;

            CurveControlPoint point = s_pointConverter.Read(ref reader, typeof(CurveControlPoint), options);
            points.Add(point);
        }

        return new CurveMap(points);
    }

    public override void Write(Utf8JsonWriter writer, CurveMap value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (CurveControlPoint point in value.Points)
        {
            s_pointConverter.Write(writer, point, options);
        }
        writer.WriteEndArray();
    }
}
