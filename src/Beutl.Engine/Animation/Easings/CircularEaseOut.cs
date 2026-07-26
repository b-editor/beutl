namespace Beutl.Animation.Easings;

public sealed class CircularEaseOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.CircularEaseOut(progress);
    }
}
