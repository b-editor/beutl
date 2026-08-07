namespace Beutl.Animation.Easings;

public sealed class BounceEaseInOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.BounceEaseInOut(progress);
    }
}
