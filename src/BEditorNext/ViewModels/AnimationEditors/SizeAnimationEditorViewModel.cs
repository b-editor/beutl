using BEditorNext.Animation;
using BEditorNext.Graphics;
using BEditorNext.ViewModels.Editors;

namespace BEditorNext.ViewModels.AnimationEditors;

public sealed class SizeAnimationEditorViewModel : AnimationEditorViewModel<Size>
{
    public SizeAnimationEditorViewModel(Animation<Size> animation, BaseEditorViewModel<Size> editorViewModel)
        : base(animation, editorViewModel)
    {
    }

    public Size Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Size(float.MaxValue, float.MaxValue));

    public Size Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Size(float.MinValue, float.MinValue));
}
