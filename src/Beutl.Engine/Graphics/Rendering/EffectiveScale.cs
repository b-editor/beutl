namespace Beutl.Graphics.Rendering;

/// <summary>
/// The supply density an operation's backing pixels actually exist at. Flows bottom-up from each
/// <see cref="RenderNodeOperation"/> so the compositor can reconcile mixed scales.
/// <c>default</c> is <see cref="Unbounded"/> (vector / re-rasterizable).
/// </summary>
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

    /// <summary>Vector / lossless sentinel: re-rasterizable at any scale. Equal to <c>default</c>.</summary>
    public static EffectiveScale Unbounded => default;

    /// <summary>
    /// A concrete bitmap density (device px per logical unit). Must be positive-finite; throws otherwise.
    /// Use <see cref="AtOrUnbounded"/> when the density is derived from animatable geometry that can go degenerate.
    /// </summary>
    public static EffectiveScale At(float scale)
        => float.IsFinite(scale) && scale > 0f
            ? new(scale, bounded: true)
            : throw new ArgumentOutOfRangeException(
                nameof(scale), scale, "EffectiveScale.At requires a positive finite density.");

    /// <summary>
    /// Non-throwing companion to <see cref="At(float)"/>: returns <see cref="Unbounded"/> for
    /// non-finite or non-positive <paramref name="scale"/>.
    /// </summary>
    public static EffectiveScale AtOrUnbounded(float scale)
        => float.IsFinite(scale) && scale > 0f ? new(scale, bounded: true) : Unbounded;

    /// <summary>True for the <see cref="Unbounded"/> (vector) sentinel.</summary>
    public bool IsUnbounded => !_bounded;

    /// <summary>The concrete density, or <c>1f</c> when <see cref="IsUnbounded"/>.</summary>
    public float Value => _bounded ? _value : 1f;
}
