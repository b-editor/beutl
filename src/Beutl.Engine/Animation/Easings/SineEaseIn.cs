namespace Beutl.Animation.Easings;

public sealed class SineEaseIn : UnitRangeEasing
{
    private const float HalfPI = MathF.PI / 2;

    public override float Ease(float progress)
    {
        return MathF.Sin((progress - 1) * HalfPI) + 1;
    }
}
