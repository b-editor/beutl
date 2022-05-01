using BeUtl.Animation;
using BeUtl.Graphics;
using BeUtl.ViewModels.Editors;

namespace BeUtl.ViewModels.AnimationEditors;

public sealed class PointAnimationEditorViewModel : AnimationEditorViewModel<Point>
{
    public PointAnimationEditorViewModel(Animation<Point> animation, EditorViewModelDescription description)
        : base(animation, description)
    {
    }

    public Point Maximum => Setter.GetMaximumOrDefault(new Point(float.MaxValue, float.MaxValue));

    public Point Minimum => Setter.GetMinimumOrDefault(new Point(float.MinValue, float.MinValue));
}
