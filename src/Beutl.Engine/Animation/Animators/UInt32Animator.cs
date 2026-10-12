namespace Beutl.Animation.Animators;

public sealed class UInt32Animator : Animator<uint>
{
    public override uint Interpolate(float progress, uint oldValue, uint newValue)
    {
        // uint's range is exact in double, so Math.Clamp alone saturates.
        double delta = (double)newValue - (double)oldValue;
        double v = oldValue + delta * progress;
        return (uint)Math.Round(Math.Clamp(v, uint.MinValue, uint.MaxValue));
    }
}
