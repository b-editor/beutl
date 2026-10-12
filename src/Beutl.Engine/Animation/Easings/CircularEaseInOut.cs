namespace Beutl.Animation.Easings;

public sealed class CircularEaseInOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        if (progress < 0.5f)
        {
            return 0.5f * (1f - MathF.Sqrt(1f - 4f * progress * progress));
        }
        else
        {
            var t = 2f * progress;
            return 0.5f * (MathF.Sqrt((3f - t) * (t - 1f)) + 1f);
        }
    }
}
