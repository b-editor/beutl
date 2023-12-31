using System.Reflection;

namespace Beutl;

public abstract class StaticProperty<T>(string name, Type ownerType, CorePropertyMetadata<T> metadata)
    : CoreProperty<T>(name, ownerType, metadata)
{
    internal abstract void RouteSetTypedValue(ICoreObject o, T? value);

    internal abstract T? RouteGetTypedValue(ICoreObject o);
}

public class StaticProperty<TOwner, T>(string name, Func<TOwner, T> getter, Action<TOwner, T>? setter, CorePropertyMetadata<T> metadata)
    : StaticProperty<T>(name, typeof(TOwner), metadata), IStaticProperty
{
    public Func<TOwner, T> Getter { get; } = getter;

    public Action<TOwner, T>? Setter { get; } = setter;

    public bool CanRead => true;

    public bool CanWrite => Setter != null;

    internal override void RouteSetTypedValue(ICoreObject o, T? value)
    {
        if (Setter == null)
        {
            throw new InvalidOperationException("This property is read-only.");
        }
        else
        {
            if (o is TOwner owner)
            {
                if (value is T typed)
                {
                    Setter(owner, typed);
                }
                else
                {
                    Setter(owner, default!);
                }
            }
        }
    }

    internal override T? RouteGetTypedValue(ICoreObject o)
    {
        if (o is TOwner owner)
        {
            return Getter(owner);
        }

        return default;
    }
}
