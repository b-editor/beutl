namespace BEditorNext.Animation.Animators;

public sealed class BoolAnimator : Animator<bool>
{
    public override bool Multiply(bool left, float right)
    {
        if (right >= 1f)
        {
            return left;
        }
        if (right >= 0)
        {
            return !left;
        }
        return left;
    }
}
