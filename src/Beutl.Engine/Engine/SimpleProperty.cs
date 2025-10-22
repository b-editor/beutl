using System.Reflection;
using Beutl.Animation;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl.Engine;

public class SimpleProperty<T>(T defaultValue, IValidator<T>? validator = null) : IProperty<T>
{
    private T _currentValue = defaultValue;
    private PropertyInfo? _propertyInfo;
    private string? _name;
    private EngineObject? _owner;

    public string Name => _name ?? throw new InvalidOperationException("Property is not initialized.");

    public Type ValueType { get; } = typeof(T);

    public bool IsAnimatable { get; } = false;

    public T DefaultValue { get; } = defaultValue;

    public T CurrentValue
    {
        get => _currentValue;
        set
        {
            var validatedValue = ValidateAndCoerce(value);
            if (!EqualityComparer<T>.Default.Equals(_currentValue, validatedValue))
            {
                var oldValue = _currentValue;
                _currentValue = validatedValue;
                HasLocalValue = true;

                ValueChanged?.Invoke(this, new PropertyValueChangedEventArgs<T>(this, oldValue, validatedValue));
            }
        }
    }

    public IAnimation<T>? Animation
    {
        get => null;
        set
        {
            if (value != null)
            {
                throw new InvalidOperationException(
                    $"Property '{Name}' does not support animations. Use Property.CreateAnimatable<T>() to create animatable properties.");
            }
        }
    }

    public bool HasLocalValue { get; private set; }

    public event EventHandler<PropertyValueChangedEventArgs<T>>? ValueChanged;

    public void operator <<= (T value)
    {
        CurrentValue = value;
    }

    public T GetValue(TimeSpan time)
    {
        return _currentValue;
    }

    public void SetPropertyInfo(PropertyInfo propertyInfo)
    {
        _propertyInfo = propertyInfo;
        _name = propertyInfo.Name;
    }

    public PropertyInfo? GetPropertyInfo() => _propertyInfo;

    public void SetOwnerObject(EngineObject? owner)
    {
        if (_owner == owner) return;

        if (owner is IModifiableHierarchical ownerHierarchical)
        {
            if (CurrentValue is IHierarchical hierarchical)
                ownerHierarchical.AddChild(hierarchical);
        }
        else if (_owner is IModifiableHierarchical oldOwnerHierarchical)
        {
            if (CurrentValue is IHierarchical hierarchical)
                oldOwnerHierarchical.RemoveChild(hierarchical);
        }

        _owner = owner;
    }

    public EngineObject? GetOwnerObject()
    {
        return _owner;
    }


    private T ValidateAndCoerce(T value)
    {
        if (validator == null)
            return value;

        if (validator.TryCoerce(new(this, null), ref value!))
        {
            return value;
        }

        return DefaultValue;
    }

    public void ResetToDefault()
    {
        CurrentValue = DefaultValue;
        HasLocalValue = false;
    }

    public bool HasValidator => validator != null;

    public override string ToString() =>
        $"{Name}: {_currentValue} (Default: {DefaultValue}, Simple)";

    public void DeserializeValue(ICoreSerializationContext context)
    {
        var optional = context.GetValue<Optional<T>>(Name);
        if (optional.HasValue)
        {
            CurrentValue = optional.Value;
        }
    }

    public void SerializeValue(ICoreSerializationContext context)
    {
        context.SetValue(Name, CurrentValue);
    }
}

public static class SimplePropertyExtensions
{
    public static IProperty<T> ToAnimatable<T>(this SimpleProperty<T> simpleProperty)
    {
        var animatableProperty = Property.CreateAnimatable(
            simpleProperty.DefaultValue);

        // 現在値を引き継ぎ
        if (simpleProperty.HasLocalValue)
        {
            animatableProperty.CurrentValue = simpleProperty.CurrentValue;
        }

        return animatableProperty;
    }
}
