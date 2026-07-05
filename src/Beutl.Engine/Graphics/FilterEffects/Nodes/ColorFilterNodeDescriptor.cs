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
    private ColorFilterNodeDescriptor(Func<SKColorFilter?> factory, object structuralToken)
    {
        Factory = factory;
        StructuralToken = structuralToken;
    }

    /// <summary>Produces the frame's <c>SKColorFilter</c> (or <see langword="null"/> for a no-op). Called at execution time.</summary>
    public Func<SKColorFilter?> Factory { get; }

    /// <summary>Identity of the filter <em>kind</em> for the structural key; equal tokens fuse and share a plan shape.</summary>
    public object StructuralToken { get; }

    /// <inheritdoc/>
    public override bool IsCoordinateInvariant => true;

    /// <inheritdoc/>
    public override BoundsContract Bounds => BoundsContract.Identity;

    /// <inheritdoc/>
    public override EffectNodeKind Kind => EffectNodeKind.ColorFilter;

    /// <summary>
    /// Builds a color-filter node. <paramref name="structuralToken"/> distinguishes filter kinds in the
    /// structural key; when omitted it is derived from <paramref name="factory"/>'s method, so filters built at
    /// the same call site (differing only in parameters) share an identity.
    /// </summary>
    public static ColorFilterNodeDescriptor Create(Func<SKColorFilter?> factory, object? structuralToken = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new ColorFilterNodeDescriptor(factory, structuralToken ?? factory.Method.MethodHandle.Value);
    }
}
