using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Beutl.Media;

namespace Beutl.Converters;

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
            else if (node.TryGetDiscriminator(out Type? type)
                && Activator.CreateInstance(type) is IJsonSerializable jsonSerializable
                && jsonSerializable is IBrush brush)
            {
                jsonSerializable.ReadFromJson(node.AsObject());
                return brush;
            }
        }

        throw new Exception("Invalid Brush");
    }

    public override void Write(Utf8JsonWriter writer, IBrush value, JsonSerializerOptions options)
    {
        if (value is IJsonSerializable jsonSerializable)
        {
            var json = new JsonObject();
            jsonSerializable.WriteToJson(json);
            json.WriteDiscriminator(value.GetType());

            json.WriteTo(writer);
        }
    }
}
