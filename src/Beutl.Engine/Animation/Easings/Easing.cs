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
}
