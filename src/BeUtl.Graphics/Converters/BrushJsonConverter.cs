using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using BeUtl.Media;

namespace BeUtl.Converters;

internal sealed class BrushJsonConverter : JsonConverter<IBrush>
{
    public override IBrush Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var node = JsonNode.Parse(ref reader);
        if (node != null)
        {
            if ((string?)node is string color)
            {
                return new SolidColorBrush(Color.Parse(color));
            }
            else if ((string?)node["@type"] is string typeStr
                && TypeFormat.ToType(typeStr) is Type type
                && Activator.CreateInstance(type) is IJsonSerializable jsonSerializable
                && jsonSerializable is IBrush brush)
            {
                jsonSerializable.ReadFromJson(node);
                return brush;
            }
        }

        throw new Exception("Invalid Brush");
    }

    public override void Write(Utf8JsonWriter writer, IBrush value, JsonSerializerOptions options)
    {
        if (value is ISolidColorBrush { Opacity: 1 } solidColorBrush)
        {
            writer.WriteStringValue(solidColorBrush.Color.ToString());
        }
        else if(value is IJsonSerializable jsonSerializable)
        {
            JsonNode json = new JsonObject();
            jsonSerializable.WriteToJson(ref json);
            json["@type"] = TypeFormat.ToString(value.GetType());

            json.WriteTo(writer);
        }
    }
}
