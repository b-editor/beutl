namespace Beutl.Animation.Easings;

public sealed class QuarticEaseInOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        if (progress < 0.5f)
        {
            var p2 = progress * progress;
            return 8f * p2 * p2;
        }
        else
        {
            var f = progress - 1f;
            var f2 = f * f;
            return -8f * f2 * f2 + 1f;
        }
    }
}
