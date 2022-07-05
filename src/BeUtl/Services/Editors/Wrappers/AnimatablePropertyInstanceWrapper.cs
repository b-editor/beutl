using BeUtl.Animation;
using BeUtl.ProjectSystem;

namespace BeUtl.Services.Editors.Wrappers;

public sealed class AnimatablePropertyInstanceWrapper<T> : PropertyInstanceWrapper<T>, IWrappedProperty<T>.IAnimatable
{
    public AnimatablePropertyInstanceWrapper(AnimatablePropertyInstance<T> pi)
        : base(pi)
    {
    }

    public IObservableList<Animation<T>> Animations => ((AnimatablePropertyInstance<T>)Tag).Children;

    IReadOnlyList<IAnimation> IWrappedProperty.IAnimatable.Animations => ((IAnimatablePropertyInstance)Tag).Children;

    public void AddAnimation(IAnimation animation)
    {
        Animations.Add((Animation<T>)animation);
    }

    public void InsertAnimation(int index, IAnimation animation)
    {
        Animations.Insert(index, (Animation<T>)animation);
    }

    public void RemoveAnimation(IAnimation animation)
    {
        Animations.Remove((Animation<T>)animation);
    }
}
