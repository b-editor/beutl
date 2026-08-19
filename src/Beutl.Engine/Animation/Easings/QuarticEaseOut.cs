namespace Beutl.Animation.Easings;

public sealed class QuarticEaseOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.QuarticEaseOut(progress);
    }
}
