using BeUtl.Animation;
using BeUtl.Media;
using BeUtl.ViewModels.Editors;

namespace BeUtl.ViewModels.AnimationEditors;

public sealed class CornerRadiusAnimationEditorViewModel : AnimationEditorViewModel<CornerRadius>
{
    public CornerRadiusAnimationEditorViewModel(Animation<CornerRadius> animation, EditorViewModelDescription description, ITimelineOptionsProvider optionsProvider)
        : base(animation, description, optionsProvider)
    {
    }

    public CornerRadius Maximum => WrappedProperty.GetMaximumOrDefault(new CornerRadius(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue));

    public CornerRadius Minimum => WrappedProperty.GetMinimumOrDefault(new CornerRadius(float.MinValue, float.MinValue, float.MinValue, float.MinValue));
}
