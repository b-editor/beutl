using BeUtl.Framework;

namespace BeUtl.Services.Editors.Wrappers;

public class CorePropertyClientImpl<T> : IAbstractProperty<T>
{
    private Type? _implementedType;
    private IObservable<T?>? _observable;

    public CorePropertyClientImpl(CoreProperty<T> property, ICoreObject obj)
    {
        Property = property;
        Object = obj;

        Header = Observable.Return(property.Name);
    }

    public ICoreObject Object { get; }

    public IObservable<string> Header { get; }

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
