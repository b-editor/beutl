namespace Beutl.Animation.Easings;

public sealed class QuarticEaseInOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.QuarticEaseInOut(progress);
    }
}
