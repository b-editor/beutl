namespace Beutl.Animation.Easings;

public sealed class LinearEasing : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.LinearEasing(progress);
    }
}
