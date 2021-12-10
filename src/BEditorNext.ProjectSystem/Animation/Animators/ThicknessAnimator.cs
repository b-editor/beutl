using BEditorNext.Graphics;

namespace BEditorNext.Animation.Animators;

public sealed class ThicknessAnimator : Animator<Thickness>
{
    public override Thickness Multiply(Thickness left, float right)
    {
        return left * right;
    }
}
