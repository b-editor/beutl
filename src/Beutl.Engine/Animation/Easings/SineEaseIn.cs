namespace Beutl.Animation.Easings;

public sealed class SineEaseIn : UnitRangeEasing
{
    public override float Ease(float progress)
    {
        return Funcs.SineEaseIn(progress);
    }
}
