namespace Beutl.Animation.Easings;

public sealed class QuinticEaseInOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.QuinticEaseInOut(progress);
    }
}
