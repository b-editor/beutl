using BeUtl.Animation;
using BeUtl.Graphics;
using BeUtl.ViewModels.Editors;

namespace BeUtl.ViewModels.AnimationEditors;

public sealed class PointAnimationEditorViewModel : AnimationEditorViewModel<Point>
{
    public PointAnimationEditorViewModel(Animation<Point> animation, EditorViewModelDescription description, ITimelineOptionsProvider optionsProvider)
        : base(animation, description, optionsProvider)
    {
    }

    public Point Maximum => WrappedProperty.GetMaximumOrDefault(new Point(float.MaxValue, float.MaxValue));

    public Point Minimum => WrappedProperty.GetMinimumOrDefault(new Point(float.MinValue, float.MinValue));
}
