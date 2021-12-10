namespace BEditorNext.Animation.Animators;

public sealed class DecimalAnimator : Animator<decimal>
{
    public override decimal Multiply(decimal left, float right)
    {
        return left * (decimal)right;
    }
}
