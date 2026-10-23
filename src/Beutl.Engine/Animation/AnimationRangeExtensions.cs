namespace Beutl.Animation;

/// <summary>
/// Resolves conservative output ranges for animation implementations.
/// </summary>
public static class AnimationRangeExtensions
{
    /// <summary>
    /// Attempts to resolve an animation's output range through the built-in
    /// <c>KeyFrameAnimation&lt;float&gt;</c> implementation.
    /// </summary>
    public static bool TryGetOutputRange<T>(
        this KeyFrameAnimation<T> animation,
        out T minimum,
        out T maximum)
        where T : IComparable<T>
    {
        ArgumentNullException.ThrowIfNull(animation);

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

    private static bool TryGetFloatRange(
        KeyFrameAnimation<float> animation,
        out float minimum,
        out float maximum)
    {
        if (animation.KeyFrames.Count == 0
            || animation.KeyFrames[0] is not KeyFrame<float> first
            || !float.IsFinite(first.Value.Value))
        {
            minimum = default;
            maximum = default;
            return false;
        }

        float minimumValue = first.Value.Value;
        float maximumValue = first.Value.Value;
        var previous = first;
        for (int i = 1; i < animation.KeyFrames.Count; i++)
        {
            if (animation.KeyFrames[i] is not KeyFrame<float> next
                || !float.IsFinite(next.Value.Value)
                || !next.Easing.Value.TryGetOutputRange(out float easingMinimum, out float easingMaximum)
                || !float.IsFinite(easingMinimum)
                || !float.IsFinite(easingMaximum)
                || easingMinimum > easingMaximum)
            {
                minimum = default;
                maximum = default;
                return false;
            }

            float delta = next.Value.Value - previous.Value.Value;
            float intervalMinimum = delta >= 0
                ? previous.Value.Value + easingMinimum * delta
                : previous.Value.Value + easingMaximum * delta;
            float intervalMaximum = delta >= 0
                ? previous.Value.Value + easingMaximum * delta
                : previous.Value.Value + easingMinimum * delta;
            if (!float.IsFinite(intervalMinimum) || !float.IsFinite(intervalMaximum))
            {
                minimum = default;
                maximum = default;
                return false;
            }

            minimumValue = Math.Min(minimumValue, Math.Min(next.Value.Value, intervalMinimum));
            maximumValue = Math.Max(maximumValue, Math.Max(next.Value.Value, intervalMaximum));
            previous = next;
        }

        minimum = minimumValue;
        maximum = maximumValue;
        return true;
    }
}
