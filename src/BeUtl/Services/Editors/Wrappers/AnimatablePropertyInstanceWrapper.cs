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
}
