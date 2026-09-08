namespace Beutl.Animation.Easings;

public sealed class CircularEaseIn : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.CircularEaseIn(progress);
    }
}
