using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Beutl;

public sealed class CorePropertyJsonConverter : JsonConverter<CoreProperty>
{
    public override CoreProperty? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var jsonNode = JsonNode.Parse(ref reader);
        if (jsonNode is JsonObject jsonObject)
        {
            string? name = (string?)jsonObject["Name"];
            string? owner = (string?)jsonObject["Owner"];
            if (name != null
                && owner != null
                && TypeFormat.ToType(owner) is { }ownerType)
            {
                return PropertyRegistry.GetRegistered(ownerType)
                    .FirstOrDefault(x => x.Name == name);
            }
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, CoreProperty value, JsonSerializerOptions options)
    {
        var json = new JsonObject
        {
            ["Name"] = value.Name,
            ["Owner"] = TypeFormat.ToString(value.OwnerType)
        };
        
        json.WriteTo(writer, options);
    }
}
