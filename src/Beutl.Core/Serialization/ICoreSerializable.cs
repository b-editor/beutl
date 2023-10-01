using System.Text.Json.Serialization;

namespace Beutl.Serialization;

[JsonConverter(typeof(CoreSerializableJsonConverter))]
public interface ICoreSerializable
{
    void Serialize(ICoreSerializationContext context);

    void Deserialize(ICoreSerializationContext context);
}
