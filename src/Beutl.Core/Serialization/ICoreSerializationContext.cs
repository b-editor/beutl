namespace Beutl.Serialization;

public interface ICoreSerializationContext
{
    CoreSerializationMode Mode { get; }

    Type OwnerType { get; }

    ISerializationErrorNotifier ErrorNotifier { get; }

    void SetValue<T>(string name, T? value);

    T? GetValue<T>(string name);

    bool Contains(string name);

    void Populate(string name, ICoreSerializable obj);
}
