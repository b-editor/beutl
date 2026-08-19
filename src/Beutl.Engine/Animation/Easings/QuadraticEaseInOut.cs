namespace Beutl.Animation.Easings;

public sealed class QuadraticEaseInOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.QuadraticEaseInOut(progress);
    }
}
