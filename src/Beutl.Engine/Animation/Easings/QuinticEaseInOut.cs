namespace Beutl.Animation.Easings;

public sealed class QuinticEaseInOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        if (progress < 0.5f)
        {
            var p2 = progress * progress;
            return 16f * p2 * p2 * progress;
        }
        else
        {
            var f = 2f * progress - 2f;
            var f2 = f * f;
            return 0.5f * f2 * f2 * f + 1f;
        }
    }
}
