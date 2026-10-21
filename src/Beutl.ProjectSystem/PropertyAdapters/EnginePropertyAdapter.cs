using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using Beutl.Collections;
using Beutl.Extensibility;

namespace Beutl.PropertyAdapters;

public class EnginePropertyAdapter<T> : IPropertyAdapter<T>, IChildObjectSource
{
    private readonly Lazy<Attribute[]> _attributes;
    private readonly Lazy<DisplayAttribute?> _displayAttribute;
    private IObservable<T?>? _observable;

    public EnginePropertyAdapter(IProperty<T> property, CoreObject obj)
    {
        _attributes = new Lazy<Attribute[]>(() => property.GetAttributes() ?? []);
        _displayAttribute = new Lazy<DisplayAttribute?>(() => _attributes.Value.FirstOrDefault(i => i is DisplayAttribute) as DisplayAttribute);
        Object = obj;
        Property = property;
    }

    public CoreObject Object { get; }

    public IProperty<T> Property { get; }

    IEnumerable<object?> IChildObjectSource.GetChildObjects() => [Property];

    [field: AllowNull, MaybeNull] public Type ImplementedType => field ??= Object.GetType();

    public Type PropertyType => Property.ValueType;

    public string DisplayName
    {
        get
        {
            var displayAttr = _displayAttribute.Value;
            return displayAttr?.GetName() ?? Property.Name;
        }
    }

    public string? Description => _displayAttribute.Value?.GetDescription();

    // A list its object keeps as a base field is changed item by item, never replaced, like Element.Objects was.
    public bool IsReadOnly => Property is not IListProperty && Property.Value is ICoreList && Object.IsBaseField(Property);

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
            .Publish(Property.Value)
            .RefCount();
    }

    public T? GetValue()
    {
        return Property.Value;
    }

    public void SetValue(T? value)
    {
        Property.Value = value!;
    }
}
