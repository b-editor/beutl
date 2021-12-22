using BEditorNext.Animation;
using BEditorNext.Media;
using BEditorNext.ViewModels.Editors;

namespace BEditorNext.ViewModels.AnimationEditors;

public sealed class PixelPointAnimationEditorViewModel : AnimationEditorViewModel<PixelPoint>
{
    public PixelPointAnimationEditorViewModel(Animation<PixelPoint> animation, BaseEditorViewModel<PixelPoint> editorViewModel)
        : base(animation, editorViewModel)
    {
    }

    public PixelPoint Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new PixelPoint(int.MaxValue, int.MaxValue));

    public PixelPoint Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new PixelPoint(int.MinValue, int.MinValue));
}
