using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using Beutl.Engine;
using Beutl.Extensibility;

namespace Beutl.Operation;

public class EnginePropertyAdapter<T> : IPropertyAdapter<T>
{
    private readonly Lazy<Attribute[]> _attributes;
    private readonly Lazy<DisplayAttribute?> _displayAttribute;
    private IObservable<T?>? _observable;

    public EnginePropertyAdapter(IProperty<T> property, EngineObject obj)
    {
        _attributes = new Lazy<Attribute[]>(() =>
        {
            var info = property.GetPropertyInfo();
            return info?.GetCustomAttributes(true).OfType<Attribute>().ToArray() ?? [];
        });
        _displayAttribute = new Lazy<DisplayAttribute?>(() => _attributes.Value.FirstOrDefault(i => i is DisplayAttribute) as DisplayAttribute);
        Object = obj;
        Property = property;
    }

    public EngineObject Object { get; }

    public IProperty<T> Property { get; }

    [field: AllowNull, MaybeNull] public Type ImplementedType => field ??= Object.GetType();

    public Type PropertyType => Property.ValueType;

    public string DisplayName
    {
        get
        {
            var info = Property.GetPropertyInfo();
            return info != null ? TypeDisplayHelpers.GetLocalizedName(info) : Property.Name;
        }
    }

    public string? Description => _displayAttribute.Value?.GetDescription();

    public bool IsReadOnly => false;

    IProperty IPropertyAdapter.GetEngineProperty() => Property;

    Attribute[] IPropertyAdapter.GetAttributes() => _attributes.Value;

    public object? GetDefaultValue()
    {
        return Property.DefaultValue;
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
