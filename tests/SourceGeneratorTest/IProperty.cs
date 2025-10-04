using System.Reflection;

namespace Beutl.Engine;

public interface IProperty
{
    string Name { get; }

    Type ValueType { get; }

    bool IsAnimatable { get; }

    bool HasLocalValue { get; }

    void SetValueAsObject(object? value);

    object? GetDefaultValueAsObject();

    void SetPropertyInfo(PropertyInfo propertyInfo);
}

public interface IProperty<T> : IProperty
{
    T DefaultValue { get; }

    T CurrentValue { get; set; }

    event EventHandler<PropertyValueChangedEventArgs<T>>? ValueChanged;
}

public class PropertyValueChangedEventArgs<T>(IProperty<T> property, T oldValue, T newValue) : EventArgs
{
    public IProperty<T> Property { get; } = property;

    public T OldValue { get; } = oldValue;

    public T NewValue { get; } = newValue;
}
