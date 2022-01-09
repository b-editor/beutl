
using BEditorNext.Animation;
using BEditorNext.Media;
using BEditorNext.ViewModels.Editors;

namespace BEditorNext.ViewModels.AnimationEditors;

public sealed class PixelRectAnimationEditorViewModel : AnimationEditorViewModel<PixelRect>
{
    public PixelRectAnimationEditorViewModel(Animation<PixelRect> animation, BaseEditorViewModel<PixelRect> editorViewModel)
        : base(animation, editorViewModel)
    {
    }

    public PixelRect Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new PixelRect(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue));

    public PixelRect Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new PixelRect(int.MinValue, int.MinValue, int.MinValue, int.MinValue));
}
