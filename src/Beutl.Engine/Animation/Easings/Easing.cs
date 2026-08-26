namespace Beutl.Animation.Easings;

public abstract class Easing
{
    public abstract float Ease(float progress);

    /// <summary>
    /// Tries to provide conservative bounds for every value <see cref="Ease"/> can return when
    /// <c>progress</c> is in the inclusive range [0, 1].
    /// </summary>
    /// <remarks>
    /// Custom easings should override this when they can prove finite bounds. Consumers must treat
    /// <see langword="false"/> as unbounded rather than estimating the range by sampling.
    /// </remarks>
    public virtual bool TryGetOutputRange(out float minimum, out float maximum)
    {
        minimum = default;
        maximum = default;
        return false;
    }

    /// <summary>
    /// Tries to provide conservative bounds for <see cref="Ease"/> over an inclusive progress
    /// subrange.
    /// </summary>
    /// <remarks>
    /// The base implementation validates <paramref name="startProgress"/> and
    /// <paramref name="endProgress"/>, then falls back to the range for the whole [0, 1] domain.
    /// Easings with tighter analytic bounds can override <see cref="TryGetOutputRangeCore"/>.
    /// </remarks>
    public bool TryGetOutputRange(
        float startProgress,
        float endProgress,
        out float minimum,
        out float maximum)
    {
        if (!float.IsFinite(startProgress) || startProgress < 0 || startProgress > 1)
            throw new ArgumentOutOfRangeException(nameof(startProgress));
        if (!float.IsFinite(endProgress) || endProgress < 0 || endProgress > 1)
            throw new ArgumentOutOfRangeException(nameof(endProgress));
        if (startProgress > endProgress)
            throw new ArgumentException("The end progress must not precede the start progress.", nameof(endProgress));

        if (startProgress == endProgress)
        {
            float value = Ease(startProgress);
            minimum = value;
            maximum = value;
            return float.IsFinite(value);
        }

        return TryGetOutputRangeCore(startProgress, endProgress, out minimum, out maximum);
    }

    /// <summary>
    /// Provides the implementation for the validated progress-subrange query.
    /// </summary>
    protected virtual bool TryGetOutputRangeCore(
        float startProgress,
        float endProgress,
        out float minimum,
        out float maximum)
    {
        return TryGetOutputRange(out minimum, out maximum);
    }
}
