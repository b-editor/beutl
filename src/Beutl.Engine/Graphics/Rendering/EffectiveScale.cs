namespace Beutl.Graphics.Rendering;

/// <summary>
/// The supply density an operation's backing pixels actually exist at, for the
/// resolution-independent pipeline (feature 003). It flows bottom-up from each
/// <see cref="RenderNodeOperation"/> so the compositor can reconcile mixed scales.
/// </summary>
/// <remarks>
/// Vector / lossless operations report <see cref="Unbounded"/>: re-rasterizable at
/// any target scale, so excluded from the supply "max" when a container resolves its
/// working scale. Bitmap-backed operations (decoded media, cached tiles, nested-scene /
/// 3D surfaces, flushed effect targets) report a concrete density via <see cref="At(float)"/>;
/// a transform re-scales it (enlarging lowers, shrinking raises) to track backing pixels
/// per logical unit.
/// <para>
/// <c>default(EffectiveScale)</c> is deliberately <see cref="Unbounded"/> (the
/// <see cref="_bounded"/> flag defaults to <see langword="false"/>), so an operation —
/// including an out-of-tree plugin op — that never sets it is treated as re-rasterizable
/// vector content rasterizing at the consumer's working scale rather than pinning an
/// arbitrary density. This inverted-flag layout is why the type is not a positional record
/// over <c>(float Value, bool IsUnbounded)</c>, whose default would wrongly be <c>At(0)</c>.
/// </para>
/// </remarks>
public readonly record struct EffectiveScale
{
    // false (the struct default) => Unbounded. true => a concrete _value.
    private readonly bool _bounded;
    private readonly float _value;

    private EffectiveScale(float value, bool bounded)
    {
        _value = value;
        _bounded = bounded;
    }

    /// <summary>
    /// The vector / lossless sentinel: re-rasterizable at any target scale and
    /// excluded from the supply max. Equal to <c>default(EffectiveScale)</c>.
    /// </summary>
    public static EffectiveScale Unbounded => default;

    /// <summary>
    /// A concrete bitmap density (device px per logical unit). <paramref name="scale"/> must be positive
    /// and finite — it later divides a buffer footprint (<see cref="Beutl.Graphics.Effects.EffectTarget.Draw"/>),
    /// so a zero/negative/NaN/∞ value is rejected here instead of producing an ∞-width or zero-area blit downstream.
    /// </summary>
    /// <remarks>
    /// <b>This throws on a non-finite / non-positive density, and the render pull path has no try/catch,
    /// so a throw aborts the whole render.</b> A <see cref="RenderNodeOperation"/> /
    /// <see cref="RenderNodeOperation.EffectiveScale"/> override that derives a density from animatable
    /// geometry (e.g. <c>At(sourcePixels / logicalWidth)</c>) can hit <c>0/0 = NaN</c> or <c>x/0 = ∞</c>
    /// on a degenerate frame (collapsed bound, off-screen clip). Such code must pre-guard the quotient
    /// (as <see cref="TransformRenderNode.RescaleDensity"/> does) or use <see cref="AtOrUnbounded"/>, which
    /// degrades a bad density to <see cref="Unbounded"/> instead of crashing the render. Reserve <c>At</c>
    /// for densities already proven finite-positive.
    /// </remarks>
    public static EffectiveScale At(float scale)
        => float.IsFinite(scale) && scale > 0f
            ? new(scale, bounded: true)
            : throw new ArgumentOutOfRangeException(
                nameof(scale), scale, "EffectiveScale.At requires a positive finite density.");

    /// <summary>
    /// The non-throwing companion to <see cref="At(float)"/>: <see cref="At(float)"/> for a positive-finite
    /// <paramref name="scale"/>, else <see cref="Unbounded"/>. Use this on the render pull path (no try/catch)
    /// when a density is derived from animatable geometry that can momentarily go degenerate — a bad frame
    /// then rasterizes at the consumer's working scale instead of aborting the export.
    /// </summary>
    public static EffectiveScale AtOrUnbounded(float scale)
        => float.IsFinite(scale) && scale > 0f ? new(scale, bounded: true) : Unbounded;

    /// <summary>True for the <see cref="Unbounded"/> (vector) sentinel.</summary>
    public bool IsUnbounded => !_bounded;

    /// <summary>The concrete density, or <c>1f</c> when <see cref="IsUnbounded"/>.</summary>
    public float Value => _bounded ? _value : 1f;
}
