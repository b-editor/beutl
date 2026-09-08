namespace Beutl.Animation.Easings;

public sealed class QuadraticEaseIn : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.QuadraticEaseIn(progress);
    }
}
