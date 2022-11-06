using System.Text.Json.Nodes;

namespace Beutl;

public interface IJsonSerializable
{
    void WriteToJson(ref JsonNode json);

    void ReadFromJson(JsonNode json);
}
