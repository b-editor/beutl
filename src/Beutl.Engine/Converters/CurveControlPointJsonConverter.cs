using System.Text.Json;
using System.Text.Json.Serialization;

using Beutl.Graphics;

namespace Beutl.Converters;

internal sealed class CurveControlPointJsonConverter : JsonConverter<CurveControlPoint>
{
    public override CurveControlPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            // Simple format: "x,y" - no handles
            string? pointString = reader.GetString();
            if (pointString != null)
            {
                Point point = Point.Parse(pointString);
                return new CurveControlPoint(point);
            }
        }
        else if (reader.TokenType == JsonTokenType.StartObject)
        {
            // Complex format with handles
            Point point = default;
            Point leftHandle = default;
            Point rightHandle = default;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string propertyName = reader.GetString()!;
                    reader.Read();

                    switch (propertyName)
                    {
                        case "point":
                        case "Point":
                            if (reader.TokenType == JsonTokenType.String)
                            {
                                string? value = reader.GetString();
                                if (value != null)
                                    point = Point.Parse(value);
                            }
                            break;
                        case "leftHandle":
                        case "LeftHandle":
                            if (reader.TokenType == JsonTokenType.String)
                            {
                                string? value = reader.GetString();
                                if (value != null)
                                    leftHandle = Point.Parse(value);
                            }
                            break;
                        case "rightHandle":
                        case "RightHandle":
                            if (reader.TokenType == JsonTokenType.String)
                            {
                                string? value = reader.GetString();
                                if (value != null)
                                    rightHandle = Point.Parse(value);
                            }
                            break;
                    }
                }
            }

            return new CurveControlPoint(point, leftHandle, rightHandle);
        }

        throw new JsonException("Invalid CurveControlPoint format.");
    }

    public override void Write(Utf8JsonWriter writer, CurveControlPoint value, JsonSerializerOptions options)
    {
        if (value.HasHandles)
        {
            // Write as object with handles
            writer.WriteStartObject();
            writer.WriteString("Point", value.Point.ToString());
            writer.WriteString("LeftHandle", value.LeftHandle.ToString());
            writer.WriteString("RightHandle", value.RightHandle.ToString());
            writer.WriteEndObject();
        }
        else
        {
            // Simple format: just the point
            writer.WriteStringValue(value.Point.ToString());
        }
    }
}
