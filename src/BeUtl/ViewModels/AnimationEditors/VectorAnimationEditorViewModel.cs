using BeUtl.Animation;
using BeUtl.Graphics;
using BeUtl.ViewModels.Editors;

namespace BeUtl.ViewModels.AnimationEditors;

public sealed class VectorAnimationEditorViewModel : AnimationEditorViewModel<Vector>
{
    public VectorAnimationEditorViewModel(AnimationSpan<Vector> animation, EditorViewModelDescription description, ITimelineOptionsProvider optionsProvider)
        : base(animation, description, optionsProvider)
    {
    }

    public Vector Maximum => WrappedProperty.GetMaximumOrDefault(new Vector(float.MaxValue, float.MaxValue));

    public Vector Minimum => WrappedProperty.GetMinimumOrDefault(new Vector(float.MinValue, float.MinValue));
}
