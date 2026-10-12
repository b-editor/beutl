namespace Beutl.Animation.Easings;

public sealed class SineEaseInOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return 0.5f * (1f - MathF.Cos(progress * MathF.PI));
    }
}
