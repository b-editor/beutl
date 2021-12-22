using BEditorNext.Animation;
using BEditorNext.Graphics;
using BEditorNext.ViewModels.Editors;

namespace BEditorNext.ViewModels.AnimationEditors;

public sealed class PointAnimationEditorViewModel : AnimationEditorViewModel<Point>
{
    public PointAnimationEditorViewModel(Animation<Point> animation, BaseEditorViewModel<Point> editorViewModel)
        : base(animation, editorViewModel)
    {
    }

    public Point Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Point(float.MaxValue, float.MaxValue));

    public Point Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Point(float.MinValue, float.MinValue));
}
