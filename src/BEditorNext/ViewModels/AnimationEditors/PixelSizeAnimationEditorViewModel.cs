using BEditorNext.Animation;
using BEditorNext.Media;
using BEditorNext.ViewModels.Editors;

namespace BEditorNext.ViewModels.AnimationEditors;

public sealed class PixelSizeAnimationEditorViewModel : AnimationEditorViewModel<PixelSize>
{
    public PixelSizeAnimationEditorViewModel(Animation<PixelSize> animation, BaseEditorViewModel<PixelSize> editorViewModel)
        : base(animation, editorViewModel)
    {
    }

    public PixelSize Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new PixelSize(int.MaxValue, int.MaxValue));

    public PixelSize Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new PixelSize(int.MinValue, int.MinValue));
}
