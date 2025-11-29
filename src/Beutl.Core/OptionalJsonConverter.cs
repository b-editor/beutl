using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Beutl.Serialization;

namespace Beutl;

public sealed class OptionalJsonConverter : JsonConverter<IOptional>
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(IOptional));
    }

    // TODO: JsonArrayに対応させる
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
            object? instance;
            if (jsonNode is JsonObject jsonObject)
            {
                instance = CoreSerializer.DeserializeFromJsonObject(jsonObject, valueType);
                goto Return;
            }

            instance = JsonSerializer.Deserialize(jsonNode, valueType, options);

        Return:
            var o = (IOptional?)Activator.CreateInstance(typeToConvert, instance);
            return o;
        }

        throw new Exception("Invalid Optional<T>");
    }

    public override void Write(Utf8JsonWriter writer, IOptional value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            object? optionalValue = value.ToObject().Value;
            Type optionalType = value.GetValueType();

            if (optionalValue is ICoreSerializable serializable)
            {
                JsonObject obj = CoreSerializer.SerializeToJsonObject(serializable);
                obj.WriteTo(writer, options);
            }
            else
            {
                JsonNode? node = JsonSerializer.SerializeToNode(optionalValue, optionalType, options);
                node?.WriteTo(writer, options);
            }
        }
    }
}
