using BeUtl.Animation;
using BeUtl.Animation.Easings;

namespace BeUtl.Services.Editors.Wrappers;

public sealed class AnimatableCorePropertyWrapper<T> : CorePropertyWrapper<T>, IWrappedProperty<T>.IAnimatable
{
    public AnimatableCorePropertyWrapper(CoreProperty<T> property, Animatable obj)
        : base(property, obj)
    {
    }

    public Animation<T> Animation => GetAnimation();

    public bool HasAnimation
    {
        get
        {
            var animatable = (Animatable)Tag;

            foreach (IAnimation item in animatable.Animations.AsSpan())
            {
                if (item.Property.Id == AssociatedProperty.Id
                    && item is Animation<T> { Children.Count: > 0 })
                {
                    return true;
                }
            }

            return false;
        }
    }

    public IAnimationSpan CreateSpan(Easing easing)
    {
        CoreProperty<T> property = AssociatedProperty;
        Type ownerType = property.OwnerType;
        ILogicalElement? owner = Animation.FindLogicalParent(ownerType);
        T? defaultValue = default;
        bool hasDefaultValue = true;
        if (owner is ICoreObject ownerCO)
        {
            defaultValue = ownerCO.GetValue(property);
        }
        else if (owner != null)
        {
            // メタデータをOverrideしている可能性があるので、owner.GetType()をする必要がある。
            CorePropertyMetadata<T> metadata = property.GetMetadata<CorePropertyMetadata<T>>(owner.GetType());
            defaultValue = metadata.DefaultValue;
            hasDefaultValue = metadata.HasDefaultValue;
        }
        else
        {
            hasDefaultValue = false;
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

    private Animation<T> GetAnimation()
    {
        var animatable = (Animatable)Tag;

        foreach (IAnimation item in animatable.Animations.AsSpan())
        {
            if (item.Property.Id == AssociatedProperty.Id
                && item is Animation<T> animation1)
            {
                return animation1;
            }
        }

        var animation2 = new Animation<T>(AssociatedProperty);
        animatable.Animations.Add(animation2);
        return animation2;
    }
}
