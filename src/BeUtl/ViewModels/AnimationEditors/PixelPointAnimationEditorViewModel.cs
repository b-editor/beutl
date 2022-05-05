using BeUtl.Animation;
using BeUtl.Media;
using BeUtl.ViewModels.Editors;

namespace BeUtl.ViewModels.AnimationEditors;

public sealed class PixelPointAnimationEditorViewModel : AnimationEditorViewModel<PixelPoint>
{
    public PixelPointAnimationEditorViewModel(Animation<PixelPoint> animation, EditorViewModelDescription description, ITimelineOptionsProvider optionsProvider)
        : base(animation, description, optionsProvider)
    {
    }

    public PixelPoint Maximum => Setter.GetMaximumOrDefault(new PixelPoint(int.MaxValue, int.MaxValue));

    public PixelPoint Minimum => Setter.GetMinimumOrDefault(new PixelPoint(int.MinValue, int.MinValue));
}
