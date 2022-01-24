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

    public PixelPoint Maximum => Setter.GetMaximumOrDefault(new PixelPoint(int.MaxValue, int.MaxValue));

    public PixelPoint Minimum => Setter.GetMinimumOrDefault(new PixelPoint(int.MinValue, int.MinValue));
}
