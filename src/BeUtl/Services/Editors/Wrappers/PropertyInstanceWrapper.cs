using BeUtl.ProjectSystem;
using BeUtl.Streaming;

namespace BeUtl.Services.Editors.Wrappers;

public static class WrappedPropertyExtensions
{
    public static CoreObject? GetObject(this IWrappedProperty property)
    {
        if (property.Tag is IPropertyInstance pi)
        {
            return pi.Parent;
        }
        else if (property.Tag is CoreObject obj)
        {
            return obj;
        }
        else if (property.Tag is ISetterDescription.IInternalSetter setter)
        {
            return setter.StreamOperator;
        }
        else
        {
            return null;
        }
    }
}

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

    public void SetValue(T? value)
    {
        ((PropertyInstance<T>)Tag).Value = value;
    }

    public T? GetValue()
    {
        return ((PropertyInstance<T>)Tag).Value;
    }
}
