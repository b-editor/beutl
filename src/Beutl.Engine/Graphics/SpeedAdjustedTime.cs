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
    /// Maps the time between <paramref name="from"/> and <paramref name="to"/> through <paramref name="speed"/>.
    /// With no animation the span is unchanged. With an empty animation it is scaled by
    /// <paramref name="currentSpeed"/> percent. Otherwise it is integrated over the animated rate.
    /// </summary>
    public static MediaTime Map(IProperty<float> speed, float currentSpeed, SpeedIntegrator integrator,
        MediaTime from, MediaTime to)
    {
        var anm = speed.GetKeyFrameAnimation();
        if (anm is not KeyFrameAnimation<float> keyFrameAnimation)
            return to - from;

        if (keyFrameAnimation.KeyFrames.Count == 0)
        {
            double factor = currentSpeed / 100d;
            return new MediaTime((long)(to.Flicks * factor)) - new MediaTime((long)(from.Flicks * factor));
        }

        integrator.EnsureCache(keyFrameAnimation);
        return integrator.IntegratePlayback(from, to);
    }

    /// <summary>
    /// Wraps <paramref name="time"/> into <c>[0, duration)</c>, counting a negative time back from the end.
    /// </summary>
    public static MediaTime WrapIntoDuration(MediaTime time, MediaTime duration)
    {
        if (time >= MediaTime.Zero)
        {
            return new MediaTime(time.Flicks % duration.Flicks);
        }

        // For negative values, add the duration before applying modulo
        long positive = duration.Flicks + (time.Flicks % duration.Flicks);
        return new MediaTime(positive % duration.Flicks);
    }
}
