using BeUtl.ProjectSystem;

namespace BeUtl.Services.Editors.Wrappers;

public class PropertyInstanceWrapper<T> : IWrappedProperty<T>
{
    private IObservable<T?>? _observable;

    public PropertyInstanceWrapper(PropertyInstance<T> pi)
    {
        AssociatedProperty = pi.Property;
        Tag = pi;

        IOperationPropertyMetadata metadata = AssociatedProperty.GetMetadata<IOperationPropertyMetadata>(pi.Parent.GetType());
        Header = metadata.Header.ToObservable(AssociatedProperty.Name);
    }

    public CoreProperty<T> AssociatedProperty { get; }

    public object Tag { get; }

    public IObservable<string> Header { get; }

    public IObservable<T?> GetObservable()
    {
        return _observable ??= ((PropertyInstance<T>)Tag).GetObservable();
    }

    public void SetValue(T value)
    {
        ((PropertyInstance<T>)Tag).Value = value;
    }
}
