namespace Beutl.Animation.Easings;

public sealed class BounceEaseOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.BounceEaseOut(progress);
    }
}
