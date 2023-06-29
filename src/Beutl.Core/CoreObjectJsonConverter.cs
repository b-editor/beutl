using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Beutl;

public sealed class CoreObjectJsonConverter : JsonConverter<ICoreObject>
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(ICoreObject));
    }

    public override ICoreObject? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var jsonNode = JsonNode.Parse(ref reader);
        if (jsonNode is JsonObject jsonObject)
        {
            Type? actualType = typeToConvert.IsSealed ? typeToConvert : jsonObject.GetDiscriminator();
            if (actualType?.IsAssignableTo(typeToConvert) == true
                && Activator.CreateInstance(actualType) is ICoreObject instance)
            {
                instance.ReadFromJson(jsonObject);

                return instance;
            }
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, ICoreObject value, JsonSerializerOptions options)
    {
        var json = new JsonObject();
        value.WriteToJson(json);
        json.WriteTo(writer, options);
    }
}
