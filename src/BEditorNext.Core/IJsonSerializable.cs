using System.Text.Json.Nodes;

namespace BEditorNext;

public interface IJsonSerializable
{
    void FromJson(JsonNode json);

    JsonNode ToJson();
}
