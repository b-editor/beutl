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
                if (_validator.TryCoerce(new ValidationContext(null, Property), ref coerced))
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

    internal override CoreProperty? Property
    {
        get => base.Property;
        set
        {
            if (value is CoreProperty<T> t)
            {
                _validator = t.GetMetadata<CorePropertyMetadata<T>>(t.OwnerType).Validator;
                base.Property = t;
                if (_validator != null)
                {
                    T? coerced = Value;
                    if (_validator.TryCoerce(new ValidationContext(null, Property), ref coerced))
                    {
                        Value = coerced!;
                    }
                }
            }
            else
            {
                _validator = null;
                base.Property = null;
            }
        }
    }

    public event EventHandler? KeyTimeChanged;

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args is CorePropertyChangedEventArgs args1
            && args.PropertyName is nameof(Value) or nameof(Easing) or nameof(KeyTime))
        {
            Invalidated?.Invoke(this, new RenderInvalidatedEventArgs(this, args1.Property.Name));

            switch (args.PropertyName)
            {
                case nameof(KeyTime):
                    KeyTimeChanged?.Invoke(this, EventArgs.Empty);
                    break;
                case nameof(Value):
                    // IAffectsRenderのイベント登録
                    if (args1.OldValue is IAffectsRender affectsRender1)
                    {
                        affectsRender1.Invalidated -= AffectsRender_Invalidated;
                    }

                    if (args1.NewValue is IAffectsRender affectsRender2)
                    {
                        affectsRender2.Invalidated += AffectsRender_Invalidated;
                    }

                    break;
            }
        }
    }

    private void AffectsRender_Invalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Invalidated?.Invoke(this, e);
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
