using System.Text.Json.Nodes;

namespace Beutl;

public interface IJsonSerializable
{
    void WriteToJson(JsonObject json);

    void ReadFromJson(JsonObject json);
}
