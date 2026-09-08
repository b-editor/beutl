namespace Beutl.Animation.Easings;

public sealed class CircularEaseInOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.CircularEaseInOut(progress);
    }
}
