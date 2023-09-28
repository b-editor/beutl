using System.Text.Json.Nodes;

using Beutl.Serialization;

namespace Beutl;

public interface IJsonSerializable
{
    [ObsoleteSerializationApi]
    void WriteToJson(JsonObject json);

    [ObsoleteSerializationApi]
    void ReadFromJson(JsonObject json);
}
