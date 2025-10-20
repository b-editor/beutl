using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using Beutl.Engine;
using Beutl.Extensibility;

namespace Beutl.Operation;

public class EnginePropertyAdapter<T>(IProperty<T> property, EngineObject obj) : IPropertyAdapter<T>
{
    private readonly Lazy<DisplayAttribute?> _displayAttribute = new(() =>
    {
        var info = property.GetPropertyInfo();
        object[]? attrs = info?.GetCustomAttributes(typeof(DisplayAttribute), true);
        return attrs?.Length > 0 ? (DisplayAttribute?)attrs[0] : null;
    });

    private IObservable<T?>? _observable;

    public EngineObject Object { get; } = obj;

    public IProperty<T> Property { get; } = property;

    [field: AllowNull, MaybeNull]
    public Type ImplementedType => field ??= Object.GetType();

    public Type PropertyType => Property.ValueType;

    public string DisplayName => _displayAttribute.Value?.GetName() ?? Property.Name;

    public string? Description => _displayAttribute.Value?.GetDescription();

    public bool IsReadOnly => false;

    public object? GetDefaultValue()
    {
        return Property.GetDefaultValueAsObject();
    }

    public IObservable<T?> GetObservable()
    {
        return _observable ??= Observable.FromEventPattern<PropertyValueChangedEventArgs<T>>(
                handler => Property.ValueChanged += handler,
                handler => Property.ValueChanged -= handler)
            .Select(e => e.EventArgs.NewValue)
            .Publish(Property.CurrentValue)
            .RefCount();
    }

    public T? GetValue()
    {
        return Property.CurrentValue;
    }

    public void SetValue(T? value)
    {
        Property.CurrentValue = value!;
    }
}
