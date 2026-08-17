namespace Beutl.Animation;

/// <summary>
/// Provides conservative bounds for built-in floating-point keyframe animations.
/// </summary>
public static class KeyFrameAnimationRange
{
    /// <summary>
    /// Attempts to return the minimum and maximum values produced by a float keyframe animation.
    /// </summary>
    /// <param name="animation">The keyframe animation to inspect.</param>
    /// <param name="minimum">The minimum output value.</param>
    /// <param name="maximum">The maximum output value.</param>
    /// <returns><see langword="true"/> when every keyframe interval has a finite, known easing range.</returns>
    public static bool TryGetOutputRange(
        KeyFrameAnimation<float> animation,
        out float minimum,
        out float maximum)
    {
        ArgumentNullException.ThrowIfNull(animation);

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
