using BeUtl.Animation;
using BeUtl.ProjectSystem;

namespace BeUtl.Services.Editors.Wrappers;

public sealed class AnimatablePropertyInstanceWrapper<T> : PropertyInstanceWrapper<T>, IWrappedProperty<T>.IAnimatable
{
    public AnimatablePropertyInstanceWrapper(AnimatablePropertyInstance<T> pi)
        : base(pi)
    {
    }

    public IObservableList<AnimationSpan<T>> Animations => ((AnimatablePropertyInstance<T>)Tag).Children;

    IReadOnlyList<IAnimationSpan> IWrappedProperty.IAnimatable.Animations => ((IAnimatablePropertyInstance)Tag).Children;

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
}
