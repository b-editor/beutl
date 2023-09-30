using System.Text.Json.Nodes;

namespace Beutl.Serialization;

public interface IJsonSerializationContext : ICoreSerializationContext
{
    JsonObject GetJsonObject();

    void SetJsonObject(JsonObject obj);

    JsonNode? GetNode(string name);

    void SetNode(string name, Type definedType, Type actualType, JsonNode? node);
}
