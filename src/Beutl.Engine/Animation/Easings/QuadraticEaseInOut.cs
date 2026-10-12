namespace Beutl.Animation.Easings;

public sealed class QuadraticEaseInOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        if (progress < 0.5f)
        {
            return 2f * progress * progress;
        }
        else
        {
            return progress * (-2f * progress + 4f) - 1f;
        }
    }
}
