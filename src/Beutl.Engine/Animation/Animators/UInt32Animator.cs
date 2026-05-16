namespace Beutl.Animation.Animators;

public sealed class UInt32Animator : Animator<uint>
{
    public override uint Interpolate(float progress, uint oldValue, uint newValue)
    {
        double delta = (double)newValue - (double)oldValue;
        double v = oldValue + delta * progress;
        return (uint)Math.Round(Math.Clamp(v, uint.MinValue, uint.MaxValue), MidpointRounding.AwayFromZero);
    }
}
