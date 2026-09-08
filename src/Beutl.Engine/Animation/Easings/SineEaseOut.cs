namespace Beutl.Animation.Easings;

public sealed class SineEaseOut : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.SineEaseOut(progress);
    }
}
