namespace Beutl.Animation.Easings;

public sealed class CubicEaseInOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.CubicEaseInOut(progress);
    }
}
