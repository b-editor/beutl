using System.Text.Json.Nodes;

namespace BeUtl;

public interface IJsonSerializable
{
    void WriteToJson(ref JsonNode json);

    void ReadFromJson(JsonNode json);
}
