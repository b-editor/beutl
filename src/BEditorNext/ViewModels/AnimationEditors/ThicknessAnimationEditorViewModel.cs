using BEditorNext.Animation;
using BEditorNext.Graphics;
using BEditorNext.ViewModels.Editors;

namespace BEditorNext.ViewModels.AnimationEditors;

public sealed class ThicknessAnimationEditorViewModel : AnimationEditorViewModel<Thickness>
{
    public ThicknessAnimationEditorViewModel(Animation<Thickness> animation, BaseEditorViewModel<Thickness> editorViewModel)
        : base(animation, editorViewModel)
    {
    }

    public Thickness Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Thickness(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue));

    public Thickness Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Thickness(float.MinValue, float.MinValue, float.MinValue, float.MinValue));
}
