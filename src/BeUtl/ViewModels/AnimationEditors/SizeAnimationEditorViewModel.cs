using BeUtl.Animation;
using BeUtl.Graphics;
using BeUtl.ViewModels.Editors;

namespace BeUtl.ViewModels.AnimationEditors;

public sealed class SizeAnimationEditorViewModel : AnimationEditorViewModel<Size>
{
    public SizeAnimationEditorViewModel(AnimationSpan<Size> animation, EditorViewModelDescription description, ITimelineOptionsProvider optionsProvider)
        : base(animation, description, optionsProvider)
    {
    }

    public Size Maximum => WrappedProperty.GetMaximumOrDefault(new Size(float.MaxValue, float.MaxValue));

    public Size Minimum => WrappedProperty.GetMinimumOrDefault(new Size(float.MinValue, float.MinValue));
}
