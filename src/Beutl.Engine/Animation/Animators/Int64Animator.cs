namespace Beutl.Animation.Animators;

public sealed class Int64Animator : Animator<long>
{
    public override long Interpolate(float progress, long oldValue, long newValue)
    {
        // double is inexact near 2^63, so the endpoints are returned directly.
        if (progress is 0f) return oldValue;
        if (progress is 1f) return newValue;
        double delta = (double)newValue - oldValue;
        double v = oldValue + delta * progress;
        // Out-of-range values are compared in double and saturated before the cast.
        if (v >= long.MaxValue) return long.MaxValue;
        if (v <= long.MinValue) return long.MinValue;
        return (long)Math.Round(v);
    }
}
