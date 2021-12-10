namespace BEditorNext.Animation.Animators;

public sealed class DoubleAnimator : Animator<double>
{
    public override double Multiply(double left, float right)
    {
        return left * right;
    }
}
