namespace Beutl.Animation.Easings;

public sealed class QuinticEaseIn : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.QuinticEaseIn(progress);
    }
}
