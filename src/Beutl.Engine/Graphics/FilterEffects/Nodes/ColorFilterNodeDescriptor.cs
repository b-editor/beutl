using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// An <c>SKColorFilter</c> node (feature 004, data-model §1). Always coordinate-invariant, so it fuses with
/// adjacent invariant nodes into the same draw via Skia's composed color-filter path. The <see cref="Factory"/>
/// is invoked per frame to produce the filter from the current parameter values; its <see cref="StructuralToken"/>
/// identifies the filter <em>kind</em> (a saturate matrix vs. a blend mode) for the structural key without pinning
/// the parameter values (an animated saturate amount re-binds without a recompile, A4).
/// </summary>
public sealed record ColorFilterNodeDescriptor : EffectNodeDescriptor
{
    internal override EffectNodeKind Kind => EffectNodeKind.ColorFilter;

    private ColorFilterNodeDescriptor(Func<SKColorFilter?> factory, object structuralToken)
    {
        Factory = factory;
        StructuralToken = structuralToken;
    }

    /// <summary>
    /// Produces an independently disposable <c>SKColorFilter</c> (or <see langword="null"/> for a no-op) for each
    /// invocation. Execution may invoke the factory more than once per frame after fan-out. Every non-null result is
    /// owned and disposed by the executor; returning a cached or previously returned instance is invalid.
    /// </summary>
    public Func<SKColorFilter?> Factory { get; }

    /// <summary>
    /// Identity of the filter <em>kind</em> for the structural key. Tokens of the same runtime type that compare equal
    /// share a plan shape; custom token implementations must keep <see cref="object.Equals(object?)"/> and
    /// <see cref="object.GetHashCode"/> stable for their lifetime.
    /// </summary>
    public object StructuralToken { get; }

    /// <inheritdoc/>
    public override bool IsCoordinateInvariant => true;

    /// <inheritdoc/>
    public override BoundsContract Bounds => BoundsContract.Identity;

    /// <summary>
    /// Builds a color-filter node. <paramref name="structuralToken"/> distinguishes filter kinds in the
    /// structural key. Each invocation must return a fresh owned filter reference or null; when the token is omitted
    /// it is derived from <paramref name="factory"/>'s method, so filters built at
    /// the same call site (differing only in parameters) share an identity.
    /// </summary>
    public static ColorFilterNodeDescriptor Create(Func<SKColorFilter?> factory, object? structuralToken = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new ColorFilterNodeDescriptor(factory, structuralToken ?? factory.Method);
    }
}
