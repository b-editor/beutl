namespace Beutl.Animation.Easings;

public sealed class BounceEaseIn : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.BounceEaseIn(progress);
    }
}
