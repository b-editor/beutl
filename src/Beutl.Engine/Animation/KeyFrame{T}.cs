using System.ComponentModel;

using Beutl.Media;
using Beutl.Validation;

namespace Beutl.Animation;

public sealed class KeyFrame<T> : KeyFrame, IKeyFrame
{
    internal static readonly Animator<T> s_animator = AnimatorRegistry.CreateAnimator<T>();

    public KeyFrame()
    {
        ValueProperty = new ValueOnlyProperty<T?>(s_animator.DefaultValue()) { Coerce = CoerceValue };
        ScanProperties<KeyFrame<T>>();
        ValueProperty.ValueChanged += OnValueChanged;
        EasingProperty.ValueChanged += (_, _) => Edited?.Invoke(this, EventArgs.Empty);
        KeyTimeProperty.ValueChanged += (_, _) =>
        {
            Edited?.Invoke(this, EventArgs.Empty);
            KeyTimeChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public IProperty<T?> ValueProperty { get; }

    public T? Value
    {
        get => ValueProperty.Value;
        set => ValueProperty.Value = value;
    }

    // The animation's validator adjusts each value set, whichever way it is set.
    private T? CoerceValue(T? value)
    {
        if (Validator != null)
        {
            T? coerced = value;
            if (Validator.TryCoerce(default, ref coerced))
            {
                return coerced!;
            }
        }

        return value;
    }

    object? IKeyFrame.Value
    {
        get => Value;
        set
        {
            if (value is T t)
            {
                Value = t;
            }
            else if (value is null
                     && (!typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) is not null))
            {
                Value = default;
            }
        }
    }

    void IKeyFrame.ReplaceValue(object? value)
    {
        if (value is T typed)
        {
            ValueProperty.SetValueEvenIfEqual(typed);
        }
        else if (value is null && (!typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) is not null))
        {
            ValueProperty.SetValueEvenIfEqual(default);
        }
        else
        {
            throw new InvalidCastException();
        }
    }

    public new IValidator<T>? Validator
    {
        get => base.Validator as IValidator<T>;
        set => base.Validator = value;
    }

    public event EventHandler? KeyTimeChanged;

    public event EventHandler? Edited;

    private void OnValueChanged(object? sender, PropertyValueChangedEventArgs<T?> e)
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Value)));
        Edited?.Invoke(this, EventArgs.Empty);

        // Resubscribe to the value's INotifyEdited.
        if (e.OldValue is INotifyEdited oldEdited)
        {
            oldEdited.Edited -= OnPropertyEdited;
        }

        if (e.NewValue is INotifyEdited newEdited)
        {
            newEdited.Edited += OnPropertyEdited;
        }
    }

    private void OnPropertyEdited(object? sender, EventArgs e)
    {
        Edited?.Invoke(this, EventArgs.Empty);
    }
}
