namespace BeUtl.Animation.Animators;

public sealed class DecimalAnimator : Animator<decimal>
{
    public override decimal Interpolate(float progress, decimal oldValue, decimal newValue)
    {
        return ((newValue - oldValue) * (decimal)progress) + oldValue;
    }
}
