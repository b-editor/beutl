namespace Beutl.Serialization;

public interface ICoreSerializable
{
    void Serialize(ICoreSerializationContext context);

    void Deserialize(ICoreSerializationContext context);
}
