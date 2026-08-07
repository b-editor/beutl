namespace Beutl.Animation.Easings;

public sealed class QuarticEaseIn : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.QuarticEaseIn(progress);
    }
}
