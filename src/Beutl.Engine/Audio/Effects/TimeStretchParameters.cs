using Beutl.Animation;
using Beutl.Engine;

namespace Beutl.Audio.Effects;

internal static class TimeStretchParameters
{
    public const float MinSpeed = 25f;
    public const float MaxSpeed = 400f;
    public const float DefaultSpeed = 100f;

    public static float Normalize(float speed)
        => float.IsNaN(speed) ? DefaultSpeed : Math.Clamp(speed, MinSpeed, MaxSpeed);

    public static double GetMinimumSpeedFactor(IProperty<float>? speed)
    {
        if (speed?.Animation is not { } animation)
            return Normalize(speed?.CurrentValue ?? DefaultSpeed) / 100d;

        if (animation.TryGetOutputRange(out float minimum, out float maximum)
            && float.IsFinite(minimum) && float.IsFinite(maximum) && minimum <= maximum)
        {
            return Normalize(minimum) / 100d;
        }

        // Even custom or overshooting animations are bounded by the processing-time clamp.
        return MinSpeed / 100d;
    }
}
