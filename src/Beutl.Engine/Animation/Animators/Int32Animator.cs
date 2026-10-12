namespace Beutl.Animation.Animators;

public sealed class Int32Animator : Animator<int>
{
    public override int Interpolate(float progress, int oldValue, int newValue)
    {
        // int's range is exact in double, so Math.Clamp alone saturates.
        double delta = (double)newValue - oldValue;
        double v = oldValue + delta * progress;
        return (int)Math.Round(Math.Clamp(v, int.MinValue, int.MaxValue));
    }
}
