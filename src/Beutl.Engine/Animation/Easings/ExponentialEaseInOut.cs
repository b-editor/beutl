namespace Beutl.Animation.Easings;

public sealed class ExponentialEaseInOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.ExponentialEaseInOut(progress);
    }
}
