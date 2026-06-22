using System;

using Beutl.Validation;

namespace Beutl.Engine;

public interface IProperty
{
    string Name { get; }

    Type ValueType { get; }

    bool IsAnimatable { get; }

    bool HasLocalValue { get; }

    void SetValueAsObject(object? value);

    object? GetDefaultValueAsObject();

    void SetAttributes(string name, Attribute[] attributes);

    void SetOwnerObject(EngineObject? owner);

    IValidator CreateValidator(Attribute[] attributes);

    void SetValidator(IValidator validator);
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
