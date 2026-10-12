namespace Beutl.Animation.Easings;

public sealed class ExponentialEaseIn : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return (progress == 0.0f) ? progress : MathF.Pow(2f, 10f * (progress - 1f));
    }
}
