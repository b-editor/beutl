namespace Beutl.Animation.Easings;

public sealed class ExponentialEaseInOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        if (progress is 0f or 1f)
            return progress;
        if (progress < 0.5f)
        {
            return 0.5f * MathF.Pow(2f, 20f * progress - 10f);
        }
        else
        {
            return -0.5f * MathF.Pow(2f, -20f * progress + 10f) + 1f;
        }
    }
}
