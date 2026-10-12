namespace Beutl.Animation.Animators;

public sealed class UInt16Animator : Animator<ushort>
{
    public override ushort Interpolate(float progress, ushort oldValue, ushort newValue)
    {
        return (ushort)SmallIntegerInterpolation.Interpolate(progress, oldValue, newValue);
    }
}
