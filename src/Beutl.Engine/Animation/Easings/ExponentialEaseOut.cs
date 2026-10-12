namespace Beutl.Animation.Easings;

public sealed class ExponentialEaseOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return (progress == 1.0f) ? progress : 1f - MathF.Pow(2f, -10f * progress);
    }
}
