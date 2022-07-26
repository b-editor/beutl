using BeUtl.Animation;

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
