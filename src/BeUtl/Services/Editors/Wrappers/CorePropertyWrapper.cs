namespace BeUtl.Services.Editors.Wrappers;

public class CorePropertyWrapper<T> : IWrappedProperty<T>
{
    private IObservable<T?>? _observable;

    public CorePropertyWrapper(CoreProperty<T> property, ICoreObject obj)
    {
        AssociatedProperty = property;
        Tag = obj;

        Header = Observable.Return(property.Name);
    }

    public CoreProperty<T> AssociatedProperty { get; }

    public object Tag { get; }

    public IObservable<string> Header { get; }

    public IObservable<T?> GetObservable()
    {
        return _observable ??= ((ICoreObject)Tag).GetObservable(AssociatedProperty);
    }

    public T? GetValue()
    {
        return ((ICoreObject)Tag).GetValue(AssociatedProperty);
    }

    public void SetValue(T? value)
    {
        ((ICoreObject)Tag).SetValue(AssociatedProperty, value);
    }
}
