﻿using Beutl.Extensibility;

namespace Beutl.Operation;

public class CorePropertyAdapter<T>(CoreProperty<T> property, ICoreObject obj) : IPropertyAdapter<T>
{
    private Type? _implementedType;
    private IObservable<T?>? _observable;

    public ICoreObject Object { get; } = obj;

    public CoreProperty<T> Property { get; } = property;

    public Type ImplementedType => _implementedType ??= Object.GetType();

    public Type PropertyType => Property.PropertyType;

    public string DisplayName
    {
        get
        {
            CorePropertyMetadata metadata = Property.GetMetadata<CorePropertyMetadata>(ImplementedType);
            return metadata.DisplayAttribute?.GetName() ?? Property.Name;
        }
    }

    public string? Description
    {
        get
        {
            CorePropertyMetadata metadata = Property.GetMetadata<CorePropertyMetadata>(ImplementedType);
            return metadata.DisplayAttribute?.GetDescription();
        }
    }

    public bool IsReadOnly => Property is IStaticProperty { CanRead: false };

    CoreProperty? IPropertyAdapter.GetCoreProperty() => Property;

    public object? GetDefaultValue()
    {
        return Property.GetMetadata<ICorePropertyMetadata>(ImplementedType).GetDefaultValue();
    }

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
