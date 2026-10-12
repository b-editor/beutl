namespace Beutl.Animation.Easings;

public sealed class CircularEaseOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return MathF.Sqrt((2f - progress) * progress);
    }
}
