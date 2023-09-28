using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Beutl;

public sealed class OptionalJsonConverter : JsonConverter<IOptional>
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(IOptional));
    }

    public override IOptional? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var jsonNode = JsonNode.Parse(ref reader);

        if (jsonNode == null)
        {
            return (IOptional?)Activator.CreateInstance(typeToConvert);
        }

        if (typeToConvert.IsGenericType
            && typeToConvert.GetGenericArguments()[0] is Type valueType)
        {
            var aa = JsonSerializer.Deserialize(jsonNode, valueType, options);
            var o= (IOptional?)Activator.CreateInstance(typeToConvert, aa);
            return o;
        }

        throw new Exception("Invalid Optional<T>");
    }

    public override void Write(Utf8JsonWriter writer, IOptional value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            JsonNode? node = JsonSerializer.SerializeToNode(value.ToObject().Value, value.GetValueType(), options);
            node?.WriteTo(writer, options);
        }
    }
}
