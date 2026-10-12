namespace Beutl.Animation.Easings;

public sealed class QuinticEaseOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        var f = progress - 1f;
        var f2 = f * f;
        return f2 * f2 * f + 1f;
    }
}
