namespace Beutl.Graphics.Rendering;

/// <summary>
/// The supply density an operation's backing pixels actually exist at, for the
/// resolution-independent pipeline (feature 003). It flows bottom-up from each
/// <see cref="RenderNodeOperation"/> so the compositor can reconcile mixed scales.
/// </summary>
/// <remarks>
/// Vector / lossless operations report <see cref="Unbounded"/> — they can be
/// re-rasterized at any target scale and are therefore excluded from the supply
/// "max" when a container resolves its working scale. Bitmap-backed operations
/// (decoded media, cached tiles, nested-scene / 3D surfaces, flushed effect
/// targets) report a concrete density via <see cref="At(float)"/>; a transform
/// re-scales that density (enlarging lowers it, shrinking raises it) so it tracks
/// the backing pixels actually available per logical unit.
/// <para>
/// <c>default(EffectiveScale)</c> is deliberately <see cref="Unbounded"/> (the
/// <see cref="_bounded"/> flag defaults to <see langword="false"/>), so an
/// operation — including an out-of-tree plugin op — that never sets it is treated
/// as re-rasterizable vector content (the safe default: it rasterizes at the
/// consumer's working scale rather than pinning it to an arbitrary density). This
/// inverted-flag layout is why the type is not a positional record over
/// <c>(float Value, bool IsUnbounded)</c>, whose default would wrongly be
/// <c>At(0)</c>.
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

    /// <summary>A concrete bitmap density (device pixels per logical unit).</summary>
    public static EffectiveScale At(float scale) => new(scale, bounded: true);

    /// <summary>True for the <see cref="Unbounded"/> (vector) sentinel.</summary>
    public bool IsUnbounded => !_bounded;

    /// <summary>The concrete density, or <c>1f</c> when <see cref="IsUnbounded"/>.</summary>
    public float Value => _bounded ? _value : 1f;
}
