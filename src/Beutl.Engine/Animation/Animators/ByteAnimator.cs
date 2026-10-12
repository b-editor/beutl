namespace Beutl.Animation.Animators;

public sealed class ByteAnimator : Animator<byte>
{
    public override byte Interpolate(float progress, byte oldValue, byte newValue)
    {
        return (byte)SmallIntegerInterpolation.Interpolate(progress, oldValue, newValue);
    }
}
