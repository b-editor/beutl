namespace BeUtl.Services.Editors.Wrappers;

public sealed class CorePropertyWrapper<T> : IWrappedProperty<T>
{
    private IObservable<T?>? _observable;

    public CorePropertyWrapper(CoreProperty<T> property, CoreObject obj)
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
        return _observable ??= ((CoreObject)Tag).GetObservable(AssociatedProperty);
    }

    public T? GetValue()
    {
        return ((CoreObject)Tag).GetValue(AssociatedProperty);
    }

    public void SetValue(T? value)
    {
        ((CoreObject)Tag).SetValue(AssociatedProperty, value);
    }
}
