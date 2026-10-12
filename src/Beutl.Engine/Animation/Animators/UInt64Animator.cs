namespace Beutl.Animation.Animators;

public sealed class UInt64Animator : Animator<ulong>
{
    public override ulong Interpolate(float progress, ulong oldValue, ulong newValue)
    {
        // double is inexact near 2^64, so the endpoints are returned directly.
        if (progress is 0f) return oldValue;
        if (progress is 1f) return newValue;
        double delta = (double)newValue - (double)oldValue;
        double v = (double)oldValue + delta * progress;
        // Out-of-range values are compared in double and saturated before the cast.
        if (v >= ulong.MaxValue) return ulong.MaxValue;
        if (v <= 0d) return 0ul;
        return (ulong)Math.Round(v);
    }
}
