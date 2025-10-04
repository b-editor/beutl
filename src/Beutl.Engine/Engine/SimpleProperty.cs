using System.Reflection;
using Beutl.Animation;
using Beutl.Validation;

namespace Beutl.Engine;

public class SimpleProperty<T> : IProperty<T>
{
    private T _currentValue;
    private readonly IValidator<T>? _validator;
    private PropertyInfo? _propertyInfo;
    private string _name;

    public SimpleProperty(T defaultValue, IValidator<T>? validator = null)
    {
        DefaultValue = defaultValue;
        _currentValue = defaultValue;
        _validator = validator;
        ValueType = typeof(T);
        IsAnimatable = false;
    }

    public string Name => _name ?? throw new InvalidOperationException("Property is not initialized.");

    public Type ValueType { get; }

    public bool IsAnimatable { get; }

    public T DefaultValue { get; }

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
                throw new InvalidOperationException($"Property '{Name}' does not support animations. Use Property.CreateAnimatable<T>() to create animatable properties.");
            }
        }
    }

    public bool HasLocalValue { get; private set; }

    public event EventHandler<PropertyValueChangedEventArgs<T>>? ValueChanged;

    public T GetValue(IClock clock)
    {
        return _currentValue;
    }

    public object? GetValueAsObject(IClock clock) => GetValue(clock);

    public void SetValueAsObject(object? value)
    {
        if (value is T typedValue)
        {
            CurrentValue = typedValue;
        }
        else if (value == null && !typeof(T).IsValueType)
        {
            CurrentValue = default(T)!;
        }
        else
        {
            // 型変換を試行
            try
            {
                if (value is IConvertible convertible && typeof(T).IsPrimitive)
                {
                    var converted = (T)Convert.ChangeType(convertible, typeof(T));
                    CurrentValue = converted;
                }
                else
                {
                    CurrentValue = DefaultValue;
                }
            }
            catch
            {
                CurrentValue = DefaultValue;
            }
        }
    }

    public object? GetDefaultValueAsObject() => DefaultValue;

    public void SetPropertyInfo(PropertyInfo propertyInfo)
    {
        _propertyInfo = propertyInfo;
        _name = propertyInfo.Name;
    }

    private T ValidateAndCoerce(T value)
    {
        if (_validator == null)
            return value;

        if (_validator.TryCoerce(new(this, null), ref value!))
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

    public bool HasValidator => _validator != null;

    public override string ToString() =>
        $"{Name}: {_currentValue} (Default: {DefaultValue}, Simple)";
}

public static class SimplePropertyExtensions
{
    public static IProperty<T> ToAnimatable<T>(this SimpleProperty<T> simpleProperty)
    {
        var animatableProperty = Property.CreateAnimatable<T>(
            simpleProperty.DefaultValue);

        // 現在値を引き継ぎ
        if (simpleProperty.HasLocalValue)
        {
            animatableProperty.CurrentValue = simpleProperty.CurrentValue;
        }

        return animatableProperty;
    }
}
