namespace Beutl.Animation.Easings;

public sealed class SineEaseInOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.SineEaseInOut(progress);
    }
}
