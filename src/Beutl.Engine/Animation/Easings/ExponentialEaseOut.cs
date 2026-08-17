namespace Beutl.Animation.Easings;

public sealed class ExponentialEaseOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.ExponentialEaseOut(progress);
    }
}
