using Beutl.Media;

namespace Beutl.Animation;

/// <summary>
/// Resolves conservative output ranges for animation implementations.
/// </summary>
public static class AnimationRangeExtensions
{
    /// <summary>
    /// Attempts to resolve an animation's output range through its provider contract or the
    /// built-in <c>KeyFrameAnimation&lt;float&gt;</c> implementation.
    /// </summary>
    public static bool TryGetOutputRange<T>(
        this IAnimation<T> animation,
        out T minimum,
        out T maximum)
        where T : IComparable<T>
    {
        ArgumentNullException.ThrowIfNull(animation);

        if (animation is IAnimationRange<T> provider)
            return provider.TryGetOutputRange(out minimum, out maximum);

        if (animation is KeyFrameAnimation<float> floatAnimation && typeof(T) == typeof(float))
        {
            bool result = TryGetFloatRange(floatAnimation, out float floatMinimum, out float floatMaximum);
            minimum = (T)(object)floatMinimum;
            maximum = (T)(object)floatMaximum;
            return result;
        }

        minimum = default!;
        maximum = default!;
        return false;
    }

    /// <summary>
    /// Attempts to resolve an animation's output range over an inclusive clock interval.
    /// </summary>
    public static bool TryGetOutputRange(
        this IAnimation<float> animation,
        TimeRange clockRange,
        out float minimum,
        out float maximum)
    {
        ArgumentNullException.ThrowIfNull(animation);
        if (clockRange.Duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(clockRange));

        if (animation is not KeyFrameAnimation<float> keyFrameAnimation)
            return animation.TryGetOutputRange(out minimum, out maximum);

        if (keyFrameAnimation.KeyFrames.Count == 0
            || keyFrameAnimation.KeyFrames[0] is not KeyFrame<float> first)
        {
            minimum = default;
            maximum = default;
            return false;
        }

        float startValue = keyFrameAnimation.Interpolate(clockRange.Start);
        float endValue = keyFrameAnimation.Interpolate(clockRange.End);
        if (!float.IsFinite(startValue) || !float.IsFinite(endValue))
        {
            minimum = default;
            maximum = default;
            return false;
        }

        float minimumValue = Math.Min(startValue, endValue);
        float maximumValue = Math.Max(startValue, endValue);
        var previous = first;
        for (int i = 1; i < keyFrameAnimation.KeyFrames.Count; i++)
        {
            if (previous.KeyTime > clockRange.End)
                break;

            if (keyFrameAnimation.KeyFrames[i] is not KeyFrame<float> next)
            {
                minimum = default;
                maximum = default;
                return false;
            }

            if (clockRange.End >= previous.KeyTime && clockRange.Start <= next.KeyTime)
            {
                if (!float.IsFinite(previous.Value) || !float.IsFinite(next.Value))
                {
                    minimum = default;
                    maximum = default;
                    return false;
                }

                long intervalTicks = (next.KeyTime - previous.KeyTime).Ticks;
                if (intervalTicks <= 0)
                {
                    minimum = default;
                    maximum = default;
                    return false;
                }

                TimeSpan overlapStart = clockRange.Start >= previous.KeyTime
                    ? clockRange.Start
                    : previous.KeyTime;
                TimeSpan overlapEnd = clockRange.End <= next.KeyTime
                    ? clockRange.End
                    : next.KeyTime;
                float startProgress = Math.Clamp(
                    (float)((overlapStart - previous.KeyTime).Ticks / (double)intervalTicks),
                    0,
                    1);
                float endProgress = Math.Clamp(
                    (float)((overlapEnd - previous.KeyTime).Ticks / (double)intervalTicks),
                    0,
                    1);
                if (!next.Easing.TryGetOutputRange(
                        startProgress,
                        endProgress,
                        out float easingMinimum,
                        out float easingMaximum)
                    || !float.IsFinite(easingMinimum)
                    || !float.IsFinite(easingMaximum)
                    || easingMinimum > easingMaximum)
                {
                    minimum = default;
                    maximum = default;
                    return false;
                }

                float delta = next.Value - previous.Value;
                float intervalMinimum = delta >= 0
                    ? previous.Value + easingMinimum * delta
                    : previous.Value + easingMaximum * delta;
                float intervalMaximum = delta >= 0
                    ? previous.Value + easingMaximum * delta
                    : previous.Value + easingMinimum * delta;
                if (!float.IsFinite(intervalMinimum) || !float.IsFinite(intervalMaximum))
                {
                    minimum = default;
                    maximum = default;
                    return false;
                }

                minimumValue = Math.Min(minimumValue, intervalMinimum);
                maximumValue = Math.Max(maximumValue, intervalMaximum);
            }

            previous = next;
        }

        minimum = minimumValue;
        maximum = maximumValue;
        return true;
    }

    private static bool TryGetFloatRange(
        KeyFrameAnimation<float> animation,
        out float minimum,
        out float maximum)
    {
        if (animation.KeyFrames.Count == 0
            || animation.KeyFrames[0] is not KeyFrame<float> first
            || !float.IsFinite(first.Value))
        {
            minimum = default;
            maximum = default;
            return false;
        }

        float minimumValue = first.Value;
        float maximumValue = first.Value;
        var previous = first;
        for (int i = 1; i < animation.KeyFrames.Count; i++)
        {
            if (animation.KeyFrames[i] is not KeyFrame<float> next
                || !float.IsFinite(next.Value)
                || !next.Easing.TryGetOutputRange(out float easingMinimum, out float easingMaximum)
                || !float.IsFinite(easingMinimum)
                || !float.IsFinite(easingMaximum)
                || easingMinimum > easingMaximum)
            {
                minimum = default;
                maximum = default;
                return false;
            }

            float delta = next.Value - previous.Value;
            float intervalMinimum = delta >= 0
                ? previous.Value + easingMinimum * delta
                : previous.Value + easingMaximum * delta;
            float intervalMaximum = delta >= 0
                ? previous.Value + easingMaximum * delta
                : previous.Value + easingMinimum * delta;
            if (!float.IsFinite(intervalMinimum) || !float.IsFinite(intervalMaximum))
            {
                minimum = default;
                maximum = default;
                return false;
            }

            minimumValue = Math.Min(minimumValue, Math.Min(next.Value, intervalMinimum));
            maximumValue = Math.Max(maximumValue, Math.Max(next.Value, intervalMaximum));
            previous = next;
        }

        minimum = minimumValue;
        maximum = maximumValue;
        return true;
    }
}
