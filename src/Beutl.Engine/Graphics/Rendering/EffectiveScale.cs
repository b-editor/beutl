namespace Beutl.Graphics.Rendering;

/// <summary>
/// Describes the device-pixel density supplied by a recorded fragment value.
/// </summary>
/// <remarks>
/// The value flows through recorded fragments and values so planning can reconcile mixed densities
/// without executing them. <c>default</c> is <see cref="Unbounded"/>, which denotes a value that can
/// be rasterized at a later selected density.
/// </remarks>
public readonly record struct EffectiveScale
{
    // Inverted flag: struct default (false) must mean Unbounded, not At(0).
    // false (the struct default) => Unbounded. true => a concrete _value.
    private readonly bool _bounded;
    private readonly float _value;

    private EffectiveScale(float value, bool bounded)
    {
        _value = value;
        _bounded = bounded;
    }

    /// <summary>Gets the re-rasterizable sentinel, which is equal to <c>default</c>.</summary>
    public static EffectiveScale Unbounded => default;

    /// <summary>
    /// Creates a concrete bitmap density in device pixels per logical unit.
    /// </summary>
    /// <param name="scale">A positive finite density.</param>
    /// <returns>A concrete effective scale.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="scale"/> is non-finite, zero, or negative.
    /// </exception>
    /// <remarks>
    /// Use <see cref="AtOrUnbounded"/> when a density derived from animatable geometry may be
    /// non-finite or non-positive.
    /// </remarks>
    public static EffectiveScale At(float scale)
        => float.IsFinite(scale) && scale > 0f
            ? new(scale, bounded: true)
            : throw new ArgumentOutOfRangeException(
                nameof(scale), scale, "EffectiveScale.At requires a positive finite density.");

    /// <summary>
    /// Creates a concrete density when possible, or returns <see cref="Unbounded"/> for an invalid density.
    /// </summary>
    /// <param name="scale">The candidate density in device pixels per logical unit.</param>
    /// <returns>
    /// A concrete effective scale when <paramref name="scale"/> is positive and finite;
    /// otherwise <see cref="Unbounded"/>.
    /// </returns>
    public static EffectiveScale AtOrUnbounded(float scale)
        => float.IsFinite(scale) && scale > 0f ? new(scale, bounded: true) : Unbounded;

    /// <summary>Gets whether this value is the <see cref="Unbounded"/> sentinel.</summary>
    public bool IsUnbounded => !_bounded;

    /// <summary>
    /// Gets the concrete density, or <c>1f</c> for <see cref="Unbounded"/> when a numeric fallback is required.
    /// </summary>
    /// <remarks>Check <see cref="IsUnbounded"/> before treating this value as a declared concrete supply.</remarks>
    public float Value => _bounded ? _value : 1f;
}
