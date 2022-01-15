using System.Text.Json.Nodes;

namespace BeUtl;

public interface IJsonSerializable
{
    void FromJson(JsonNode json);

    JsonNode ToJson();
}
