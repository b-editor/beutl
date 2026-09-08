namespace Beutl.Animation.Easings;

public sealed class QuinticEaseOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.QuinticEaseOut(progress);
    }
}
