namespace Beutl.Animation.Easings;

public sealed class CubicEaseIn : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.CubicEaseIn(progress);
    }
}
