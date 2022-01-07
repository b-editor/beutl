using BEditorNext.Animation;
using BEditorNext.Media;
using BEditorNext.ViewModels.Editors;

namespace BEditorNext.ViewModels.AnimationEditors;

public sealed class CornerRadiusAnimationEditorViewModel : AnimationEditorViewModel<CornerRadius>
{
    public CornerRadiusAnimationEditorViewModel(Animation<CornerRadius> animation, BaseEditorViewModel<CornerRadius> editorViewModel)
        : base(animation, editorViewModel)
    {
    }

    public CornerRadius Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new CornerRadius(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue));

    public CornerRadius Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new CornerRadius(float.MinValue, float.MinValue, float.MinValue, float.MinValue));
}
