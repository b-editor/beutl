using BEditorNext.Graphics;

namespace BEditorNext.Animation.Animators;

public sealed class RectAnimator : Animator<Rect>
{
    public override Rect Multiply(Rect left, float right)
    {
        return left * right;
    }
}
