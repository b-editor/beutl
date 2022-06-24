using BeUtl.Animation;
using BeUtl.Media;
using BeUtl.ViewModels.Editors;

namespace BeUtl.ViewModels.AnimationEditors;

public sealed class PixelSizeAnimationEditorViewModel : AnimationEditorViewModel<PixelSize>
{
    public PixelSizeAnimationEditorViewModel(Animation<PixelSize> animation, EditorViewModelDescription description, ITimelineOptionsProvider optionsProvider)
        : base(animation, description, optionsProvider)
    {
    }

    public PixelSize Maximum => WrappedProperty.GetMaximumOrDefault(new PixelSize(int.MaxValue, int.MaxValue));

    public PixelSize Minimum => WrappedProperty.GetMinimumOrDefault(new PixelSize(int.MinValue, int.MinValue));
}
