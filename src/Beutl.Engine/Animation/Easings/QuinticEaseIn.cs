namespace Beutl.Animation.Easings;

public sealed class QuinticEaseIn : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        var p2 = progress * progress;
        return p2 * p2 * progress;
    }
}
