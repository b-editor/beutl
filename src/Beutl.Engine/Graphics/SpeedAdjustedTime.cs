using Beutl.Animation;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Graphics;

/// <summary>
/// The time arithmetic drawables share when they play their content at the rate a <c>Speed</c> property sets.
/// </summary>
internal static class SpeedAdjustedTime
{
    /// <summary>
    /// Maps <paramref name="time"/> through <paramref name="speed"/>: unchanged without a keyframe animation, scaled
    /// by <paramref name="currentSpeed"/> percent when the animation has no keyframes, and integrated over the
    /// animated rate otherwise.
    /// </summary>
    public static TimeSpan Map(IProperty<float> speed, float currentSpeed, SpeedIntegrator integrator, TimeSpan time)
    {
        var anm = speed.Animation;
        if (anm is not KeyFrameAnimation<float> keyFrameAnimation)
            return time;

        if (keyFrameAnimation.KeyFrames.Count == 0)
        {
            return TimeSpan.FromTicks((long)(time.Ticks * (currentSpeed / 100.0)));
        }

        integrator.EnsureCache(anm);
        return integrator.Integrate(time, keyFrameAnimation);
    }

    /// <summary>
    /// Wraps <paramref name="time"/> into <c>[0, duration)</c>, counting a negative time back from the end.
    /// </summary>
    public static TimeSpan WrapIntoDuration(TimeSpan time, TimeSpan duration)
    {
        if (time >= TimeSpan.Zero)
        {
            return TimeSpan.FromTicks(time.Ticks % duration.Ticks);
        }

        // For negative values, add the duration before applying modulo
        var positiveTicks = duration.Ticks + (time.Ticks % duration.Ticks);
        return TimeSpan.FromTicks(positiveTicks % duration.Ticks);
    }
}
