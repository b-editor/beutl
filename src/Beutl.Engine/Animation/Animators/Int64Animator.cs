namespace Beutl.Animation.Animators;

public sealed class Int64Animator : Animator<long>
{
    public override long Interpolate(float progress, long oldValue, long newValue)
    {
        double delta = (double)newValue - oldValue;
        double v = oldValue + delta * progress;
        return (long)Math.Round(Math.Clamp(v, long.MinValue, long.MaxValue), MidpointRounding.AwayFromZero);
    }
}
