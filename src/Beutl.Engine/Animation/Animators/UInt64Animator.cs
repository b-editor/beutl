namespace Beutl.Animation.Animators;

public sealed class UInt64Animator : Animator<ulong>
{
    public override ulong Interpolate(float progress, ulong oldValue, ulong newValue)
    {
        double delta = (double)newValue - (double)oldValue;
        double v = (double)oldValue + delta * progress;
        return (ulong)Math.Round(Math.Clamp(v, ulong.MinValue, ulong.MaxValue), MidpointRounding.AwayFromZero);
    }
}
