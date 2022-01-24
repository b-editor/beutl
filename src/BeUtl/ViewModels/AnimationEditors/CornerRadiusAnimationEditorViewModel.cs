using BeUtl.Animation;
using BeUtl.Media;
using BeUtl.ViewModels.Editors;

namespace BeUtl.ViewModels.AnimationEditors;

public sealed class CornerRadiusAnimationEditorViewModel : AnimationEditorViewModel<CornerRadius>
{
    public CornerRadiusAnimationEditorViewModel(Animation<CornerRadius> animation, BaseEditorViewModel<CornerRadius> editorViewModel)
        : base(animation, editorViewModel)
    {
    }

    public CornerRadius Maximum => Setter.GetMaximumOrDefault(new CornerRadius(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue));

    public CornerRadius Minimum => Setter.GetMinimumOrDefault(new CornerRadius(float.MinValue, float.MinValue, float.MinValue, float.MinValue));
}
