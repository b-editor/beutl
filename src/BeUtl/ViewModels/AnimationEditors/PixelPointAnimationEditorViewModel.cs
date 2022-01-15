using BeUtl.Animation;
using BeUtl.Media;
using BeUtl.ViewModels.Editors;

namespace BeUtl.ViewModels.AnimationEditors;

public sealed class PixelPointAnimationEditorViewModel : AnimationEditorViewModel<PixelPoint>
{
    public PixelPointAnimationEditorViewModel(Animation<PixelPoint> animation, BaseEditorViewModel<PixelPoint> editorViewModel)
        : base(animation, editorViewModel)
    {
    }

    public PixelPoint Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new PixelPoint(int.MaxValue, int.MaxValue));

    public PixelPoint Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new PixelPoint(int.MinValue, int.MinValue));
}
