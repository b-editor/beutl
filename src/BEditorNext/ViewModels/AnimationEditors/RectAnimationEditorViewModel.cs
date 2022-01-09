
using BEditorNext.Animation;
using BEditorNext.Graphics;
using BEditorNext.ViewModels.Editors;

namespace BEditorNext.ViewModels.AnimationEditors;

public sealed class RectAnimationEditorViewModel : AnimationEditorViewModel<Rect>
{
    public RectAnimationEditorViewModel(Animation<Rect> animation, BaseEditorViewModel<Rect> editorViewModel)
        : base(animation, editorViewModel)
    {
    }

    public Rect Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Rect(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue));

    public Rect Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Rect(float.MinValue, float.MinValue, float.MinValue, float.MinValue));
}
