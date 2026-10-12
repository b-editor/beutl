namespace Beutl.Animation.Easings;

public sealed class CubicEaseOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        var f = progress - 1f;
        return f * f * f + 1f;
    }
}
