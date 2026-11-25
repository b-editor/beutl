using Beutl.Collections;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.Editor.Services;

public static class ObjectRegenerator
{
    public static void Regenerate<T>(T obj, out T newInstance)
        where T : class, ICoreObject, new()
    {
        newInstance = (T)ObjectRegeneration.Copy(obj, typeof(T));
    }

    // The JSON keeps what copies renew, such as AI nodes' request keys; only the overloads that build objects renew it.
    public static void Regenerate(ICoreSerializable obj, out string json)
    {
        json = ObjectRegeneration.ToJson(obj);
    }

    public static void Regenerate(ICoreSerializable obj, Type actualType, out ICoreSerializable newInstance)
    {
        newInstance = ObjectRegeneration.Copy(obj, actualType);
    }

    public static void Regenerate<T>(T[] obj, out T[] newInstance)
        where T : class, ICoreObject, new()
    {
        var wrapper = new ListWrapper<T>();
        wrapper.Items.AddRange(obj);
        Regenerate(wrapper, out ListWrapper<T> copy);
        newInstance = copy.Items.ToArray();
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
