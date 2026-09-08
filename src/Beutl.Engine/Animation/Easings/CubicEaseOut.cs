namespace Beutl.Animation.Easings;

public sealed class CubicEaseOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.CubicEaseOut(progress);
    }
}
