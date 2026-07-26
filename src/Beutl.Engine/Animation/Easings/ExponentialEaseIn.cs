namespace Beutl.Animation.Easings;

public sealed class ExponentialEaseIn : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.ExponentialEaseIn(progress);
    }
}
