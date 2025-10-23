using System.ComponentModel;

using Beutl.Media;
using Beutl.Validation;

namespace Beutl.Animation;

public sealed class KeyFrame<T> : KeyFrame, IKeyFrame
{
    public static readonly CoreProperty<T?> ValueProperty;
    internal static readonly Animator<T> s_animator;
    private T? _value;
    internal IValidator<T>? _validator;
    private IKeyFrameAnimation? _parent;

    public KeyFrame()
    {
        _value = s_animator.DefaultValue();
    }

    static KeyFrame()
    {
        s_animator = AnimatorRegistry.CreateAnimator<T>();

        ValueProperty = ConfigureProperty<T?, KeyFrame<T>>(nameof(Value))
            .Accessor(o => o.Value, (o, v) => o.Value = v)
            .Register();
    }

    public T? Value
    {
        get => _value;
        set
        {
            if (_validator != null)
            {
                T? coerced = value;
                if (_validator.TryCoerce(default, ref coerced))
                {
                    value = coerced!;
                }
            }

            SetAndRaise(ValueProperty, ref _value, value);
        }
    }

    object? IKeyFrame.Value
    {
        get => Value;
        set
        {
            if (value is T t)
                Value = t;
        }
    }

    public new IValidator<T>? Validator
    {
        get => base.Validator as IValidator<T>;
        set => base.Validator = value;
    }

    public event EventHandler? KeyTimeChanged;

    public event EventHandler? Edited;

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args is CorePropertyChangedEventArgs args1
            && args.PropertyName is nameof(Value) or nameof(Easing) or nameof(KeyTime))
        {
            Edited?.Invoke(this, EventArgs.Empty);

            switch (args.PropertyName)
            {
                case nameof(KeyTime):
                    KeyTimeChanged?.Invoke(this, EventArgs.Empty);
                    break;
                case nameof(Value):
                    // IAffectsRenderのイベント登録
                    if (args1.OldValue is INotifyEdited oldEdited)
                    {
                        oldEdited.Edited -= OnPropertyEdited;
                    }

                    if (args1.NewValue is INotifyEdited newEdited)
                    {
                        newEdited.Edited += OnPropertyEdited;
                    }

                    break;
            }
        }
    }

    private void OnPropertyEdited(object? sender, EventArgs e)
    {
        Edited?.Invoke(this, EventArgs.Empty);
    }

    void IKeyFrame.SetParent(IKeyFrameAnimation? parent)
    {
        _parent = parent;
    }

    IKeyFrameAnimation? IKeyFrame.GetParent()
    {
        return _parent;
    }

    //void IKeyFrame.SetDuration(TimeSpan timeSpan)
    //{
    //    Duration = timeSpan;
    //}
}
