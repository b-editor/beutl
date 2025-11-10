using Beutl.IO;

namespace Beutl.Serialization;

public interface ICoreSerializationContext
{
    CoreSerializationMode Mode { get; }

    IFileSystem FileSystem { get; }

    Uri? BaseUri { get; }

    Type OwnerType { get; }

    void SetValue<T>(string name, T? value);

    T? GetValue<T>(string name);

    bool Contains(string name);

    void Populate(string name, ICoreSerializable obj);

    void Resolve(Guid id, Action<ICoreSerializable> callback);
}
