namespace Beutl.Animation.Easings;

public sealed class CubicEaseInOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        if (progress < 0.5f)
        {
            return 4f * progress * progress * progress;
        }
        else
        {
            var f = 2f * (progress - 1);
            return 0.5f * f * f * f + 1f;
        }
    }
}
