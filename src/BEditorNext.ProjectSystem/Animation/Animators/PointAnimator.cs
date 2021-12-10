using BEditorNext.Graphics;

namespace BEditorNext.Animation.Animators;

public sealed class PointAnimator : Animator<Point>
{
    public override Point Multiply(Point left, float right)
    {
        return left * right;
    }
}
