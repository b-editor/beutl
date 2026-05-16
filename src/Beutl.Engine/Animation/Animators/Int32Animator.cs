namespace Beutl.Animation.Animators;

public sealed class Int32Animator : Animator<int>
{
    public override int Interpolate(float progress, int oldValue, int newValue)
    {
        double delta = (double)newValue - oldValue;
        double v = oldValue + delta * progress;
        return (int)Math.Round(Math.Clamp(v, int.MinValue, int.MaxValue), MidpointRounding.AwayFromZero);
    }
}
