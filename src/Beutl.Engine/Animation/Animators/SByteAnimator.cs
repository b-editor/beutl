namespace Beutl.Animation.Animators;

public sealed class SByteAnimator : Animator<sbyte>
{
    public override sbyte Interpolate(float progress, sbyte oldValue, sbyte newValue)
    {
        return (sbyte)SmallIntegerInterpolation.Interpolate(progress, oldValue, newValue);
    }
}
