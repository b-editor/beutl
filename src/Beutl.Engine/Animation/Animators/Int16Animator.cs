namespace Beutl.Animation.Animators;

public sealed class Int16Animator : Animator<short>
{
    public override short Interpolate(float progress, short oldValue, short newValue)
    {
        return (short)SmallIntegerInterpolation.Interpolate(progress, oldValue, newValue);
    }
}
