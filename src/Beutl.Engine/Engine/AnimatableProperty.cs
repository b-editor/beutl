using System.Diagnostics;
using System.Reflection;
using Beutl.Animation;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl.Engine;

public class AnimatableProperty<T> : IProperty<T>
{
    private T _currentValue;
    private IAnimation<T>? _animation;
    private readonly IValidator<T>? _validator;
    private PropertyInfo? _propertyInfo;
    private string _name;
    private EngineObject? _owner;

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
                if (_owner is IModifiableHierarchical ownerHierarchical)
                {
                    if (oldValue is IHierarchical oldHierarchical)
                        ownerHierarchical.RemoveChild(oldHierarchical);

                    if (validatedValue is IHierarchical newHierarchical)
                        ownerHierarchical.AddChild(newHierarchical);
                }
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
                var oldValue = _animation;
                _animation = value;

                if (_animation != null && _validator != null)
                {
                    _animation.Validator = _validator;
                }

                AnimationChanged?.Invoke(_animation!);
                if (_owner is IModifiableHierarchical ownerHierarchical)
                {
                    if (oldValue is IHierarchical oldHierarchical)
                        ownerHierarchical.RemoveChild(oldHierarchical);

                    if (value is IHierarchical newHierarchical)
                        ownerHierarchical.AddChild(newHierarchical);
                }
            }
        }
    }

    public bool HasLocalValue { get; private set; }

    public event EventHandler<PropertyValueChangedEventArgs<T>>? ValueChanged;

    public event Action<IAnimation<T>?>? AnimationChanged;

    public void operator <<= (T value)
    {
        CurrentValue = value;
    }

    public T GetValue(TimeSpan time)
    {
        try
        {
            T value;

            // アニメーション値を優先
            if (_animation != null)
            {
                value = _animation.GetAnimatedValue(time) ?? _currentValue;

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

            if (Animation != null)
                ownerHierarchical.AddChild(Animation);
        }
        else if (_owner is IModifiableHierarchical oldOwnerHierarchical)
        {
            if (CurrentValue is IHierarchical hierarchical)
                oldOwnerHierarchical.RemoveChild(hierarchical);

            if (Animation != null)
                oldOwnerHierarchical.RemoveChild(Animation);
        }

        _owner = owner;
    }

    public EngineObject? GetOwnerObject()
    {
        return _owner;
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
