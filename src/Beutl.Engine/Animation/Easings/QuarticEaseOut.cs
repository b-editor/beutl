namespace Beutl.Animation.Easings;

public sealed class QuarticEaseOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        var f = progress - 1f;
        var f2 = f * f;
        return -f2 * f2 + 1f;
    }
}
