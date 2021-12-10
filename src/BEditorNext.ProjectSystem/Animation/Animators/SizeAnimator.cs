using BEditorNext.Graphics;

namespace BEditorNext.Animation.Animators;

public sealed class SizeAnimator : Animator<Size>
{
    public override Size Multiply(Size left, float right)
    {
        return left * right;
    }
}
