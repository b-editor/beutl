using System.Reflection;

namespace BeUtl;

public abstract class StaticProperty<T> : CoreProperty<T>
{
    public StaticProperty(string name, Type ownerType, CorePropertyMetadata<T> metadata)
        : base(name, ownerType, metadata)
    {
    }

    internal abstract void RouteSetTypedValue(ICoreObject o, T? value);

    internal abstract T? RouteGetTypedValue(ICoreObject o);
}

public class StaticProperty<TOwner, T> : StaticProperty<T>, IStaticProperty
{
    public StaticProperty(string name, Func<TOwner, T> getter, Action<TOwner, T>? setter, CorePropertyMetadata<T> metadata)
        : base(name, typeof(TOwner), metadata)
    {
        Getter = getter;
        Setter = setter;
    }

    public Func<TOwner, T> Getter { get; }

    public Action<TOwner, T>? Setter { get; }

    public bool CanRead => true;

    public bool CanWrite => Setter != null;

    internal override void RouteSetTypedValue(ICoreObject o, T? value)
    {
        if (Setter == null)
        {
            throw new Exception("This property is read-only.");
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
