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
    public static MediaTime Map(IProperty<float> speed, float currentSpeed, SpeedIntegrator integrator, MediaTime time)
    {
        var anm = speed.GetKeyFrameAnimation();
        if (anm is not KeyFrameAnimation<float> keyFrameAnimation)
            return time;

        if (keyFrameAnimation.KeyFrames.Count == 0)
        {
            return new MediaTime((long)(time.Flicks * (currentSpeed / 100.0)));
        }

        integrator.EnsureCache(anm);
        return integrator.Integrate(time, keyFrameAnimation);
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
