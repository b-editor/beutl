using BeUtl.Animation;
using BeUtl.Animation.Easings;
using BeUtl.Streaming;
using BeUtl.Styling;

namespace BeUtl.Services.Editors.Wrappers;

public interface IStylingSetterWrapper : IWrappedProperty
{
}

public sealed class StylingSetterWrapper<T> : IWrappedProperty<T>.IAnimatable, IStylingSetterWrapper
{
    public StylingSetterWrapper(Setter<T> setter)
    {
        AssociatedProperty = setter.Property;
        Tag = setter;

        Header = Observable.Return(setter.Property.Name);
    }

    public CoreProperty<T> AssociatedProperty { get; }

    public object Tag { get; }

    public IObservable<string> Header { get; }

    public Animation<T> Animation
    {
        get
        {
            var setter = (Setter<T>)Tag;
            setter.Animation ??= new Animation<T>(AssociatedProperty);
            return setter.Animation;
        }
    }

    public bool HasAnimation
    {
        get
        {
            var setter = (Setter<T>)Tag;
            return setter.Animation is { Children.Count: > 0 };
        }
    }

    public IObservable<T?> GetObservable()
    {
        return (Setter<T>)Tag;
    }

    public T? GetValue()
    {
        return ((Setter<T>)Tag).Value;
    }

    public void SetValue(T? value)
    {
        ((Setter<T>)Tag).Value = value;
    }

    IAnimationSpan IWrappedProperty.IAnimatable.CreateSpan(Easing easing)
    {
        CoreProperty<T> property = AssociatedProperty;
        IStyle? style = Animation.FindStylingParent<IStyle>();
        T? defaultValue = GetValue();
        bool hasDefaultValue = true;
        if (style != null && defaultValue == null)
        {
            // メタデータをOverrideしている可能性があるので、owner.GetType()をする必要がある。
            CorePropertyMetadata<T> metadata = property.GetMetadata<CorePropertyMetadata<T>>(style.TargetType);
            defaultValue = metadata.DefaultValue;
            hasDefaultValue = metadata.HasDefaultValue;
        }

        var span = new AnimationSpan<T>
        {
            Easing = easing,
            Duration = TimeSpan.FromSeconds(2)
        };

        if (hasDefaultValue && defaultValue != null)
        {
            span.Previous = defaultValue;
            span.Next = defaultValue;
        }

        return span;
    }
}
