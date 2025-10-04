using System.Diagnostics;
using System.Reflection;
using Beutl.Animation;
using Beutl.Validation;

namespace Beutl.Engine;

public class AnimatableProperty<T> : IProperty<T>
{
    private T _currentValue;
    private IAnimation<T>? _animation;
    private readonly IValidator<T>? _validator;
    private PropertyInfo? _propertyInfo;
    private string _name;

    public AnimatableProperty(T defaultValue, IValidator<T>? validator = null)
    {
        DefaultValue = defaultValue;
        _currentValue = defaultValue;
        _validator = validator;
        ValueType = typeof(T);
        IsAnimatable = true;
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
        get => _animation;
        set
        {
            if (_animation != value)
            {
                _animation = value;

                if (_animation != null && _validator != null)
                {
                    _animation.Validator = _validator;
                }
            }
        }
    }

    public bool HasLocalValue { get; private set; }

    public event EventHandler<PropertyValueChangedEventArgs<T>>? ValueChanged;

    public T GetValue(IClock clock)
    {
        try
        {
            T value;

            // アニメーション値を優先
            if (_animation != null)
            {
                value = _animation.GetAnimatedValue(clock) ?? _currentValue;

                // アニメーション値もバリデーション
                value = ValidateAndCoerce(value);
            }
            else
            {
                // アニメーションがない場合は現在値
                value = _currentValue;
            }

            return value;
        }
        catch (Exception ex)
        {
            // エラー時は安全なデフォルト値を返す
            Debug.WriteLine($"Error getting value for property '{Name}': {ex.Message}");
            return DefaultValue;
        }
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
                if (value is IConvertible convertible && typeof(T).IsAssignableFrom(typeof(IConvertible)))
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
        Animation = null;
    }
}
