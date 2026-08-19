namespace Beutl.Animation.Easings;

public sealed class QuadraticEaseOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.QuadraticEaseOut(progress);
    }
}
