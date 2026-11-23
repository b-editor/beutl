using System.Text;
using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Utilities;

namespace Beutl.Editor.Services;

public static class ObjectRegenerator
{
    public static void Regenerate<T>(T obj, out T newInstance)
        where T : class, ICoreObject, new()
    {
        using var output = new PooledArrayBufferWriter<byte>(ObjectRegeneration.BufferSizeDefault);
        ObjectRegeneration.Write(obj, output);

        Span<byte> buffer = ObjectRegeneration.GetWrittenSpan(output);

        JsonObject jsonObj = ObjectRegeneration.ParseRegeneratedBuffer(buffer);
        var instance = new T();

        CoreSerializer.PopulateFromJsonObject(instance, jsonObj);
        ObjectRegeneration.RenewCopiedValues(instance);

        newInstance = instance;
    }

    // The JSON keeps what copies renew, such as AI nodes' request keys; only the overloads that build objects renew it.
    public static void Regenerate(ICoreSerializable obj, out string json)
    {
        using var output = new PooledArrayBufferWriter<byte>(ObjectRegeneration.BufferSizeDefault);
        ObjectRegeneration.Write(obj, output);

        Span<byte> buffer = ObjectRegeneration.GetWrittenSpan(output);
        json = Encoding.UTF8.GetString(buffer);
    }

    public static void Regenerate(ICoreSerializable obj, Type actualType, out ICoreSerializable newInstance)
    {
        newInstance = ObjectRegeneration.Copy(obj, actualType);
    }

    public static void Regenerate<T>(T[] obj, out T[] newInstance)
        where T : class, ICoreObject, new()
    {
        using var output = new PooledArrayBufferWriter<byte>(ObjectRegeneration.BufferSizeDefault);
        var wrapper = new ListWrapper<T>();
        wrapper.Items.AddRange(obj);
        ObjectRegeneration.Write(wrapper, output);

        Span<byte> buffer = ObjectRegeneration.GetWrittenSpan(output);

        JsonObject jsonObj = ObjectRegeneration.ParseRegeneratedBuffer(buffer);
        var instance = new ListWrapper<T>();

        CoreSerializer.PopulateFromJsonObject(instance, jsonObj);
        ObjectRegeneration.RenewCopiedValues(instance);

        newInstance = instance.Items.ToArray();
    }

    public sealed class ListWrapper<T> : CoreObject
    {
        public ListWrapper()
        {
            ScanProperties<ListWrapper<T>>();
        }

        public IListProperty<T> Items { get; } = new ListProperty<T> { AffectsRender = false };
    }
}
