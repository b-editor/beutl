using BeUtl.Animation;

namespace BeUtl.Services.Editors.Wrappers;

public sealed class AnimatableCorePropertyWrapper<T> : CorePropertyWrapper<T>, IWrappedProperty<T>.IAnimatable
{
    public AnimatableCorePropertyWrapper(CoreProperty<T> property, Animatable obj)
        : base(property, obj)
    {
    }

    public IObservableList<AnimationSpan<T>> Animations
        => GetAnimation().Children;

    IReadOnlyList<IAnimationSpan> IWrappedProperty.IAnimatable.Animations
        => ((IAnimation)GetAnimation()).Children;

    public void AddAnimation(IAnimationSpan animation)
    {
        Animations.Add((AnimationSpan<T>)animation);
    }

    public void InsertAnimation(int index, IAnimationSpan animation)
    {
        Animations.Insert(index, (AnimationSpan<T>)animation);
    }

    public void RemoveAnimation(IAnimationSpan animation)
    {
        Animations.Remove((AnimationSpan<T>)animation);
    }

    private Animation<T> GetAnimation()
    {
        var animatable = (Animatable)Tag;

        foreach (var item in animatable.Animations)
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
