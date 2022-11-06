using Beutl.Framework;

namespace Beutl.Operators.Configure;

public class CorePropertyImpl<T> : IAbstractProperty<T>
{
    private Type? _implementedType;
    private IObservable<T?>? _observable;

    public CorePropertyImpl(CoreProperty<T> property, ICoreObject obj)
    {
        Property = property;
        Object = obj;
    }

    public ICoreObject Object { get; }

    public CoreProperty<T> Property { get; }

    public Type ImplementedType => _implementedType ??= Object.GetType();

    public IObservable<T?> GetObservable()
    {
        return _observable ??= Object.GetObservable(Property);
    }

    public T? GetValue()
    {
        return Object.GetValue(Property);
    }

    public void SetValue(T? value)
    {
        Object.SetValue(Property, value);
    }
}
