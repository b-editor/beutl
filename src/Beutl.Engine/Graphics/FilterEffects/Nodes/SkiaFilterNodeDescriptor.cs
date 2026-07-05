using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// An <c>SKImageFilter</c> node (feature 004, data-model §1): blur, drop-shadow, morphology, matrix transform,
/// convolution, etc. Adjacent Skia-filter nodes group into one filtered draw (C2), reproducing today's
/// <c>SKImageFilterBuilder</c> accumulation as an explicit plan pass. Non-invariant: it carries a mandatory
/// <see cref="BoundsContract"/>. The <see cref="Factory"/> composes the frame's filter over the upstream filter
/// (<see langword="null"/> when this node heads the group); its <see cref="StructuralToken"/> identifies the
/// filter kind for the structural key, leaving the parameters (sigma, offset, kernel) as re-resolved sizes (A4).
/// </summary>
public sealed record SkiaFilterNodeDescriptor : EffectNodeDescriptor
{
    private SkiaFilterNodeDescriptor(
        Func<SKImageFilter?, SKImageFilter?> factory, BoundsContract bounds, object structuralToken)
    {
        Factory = factory;
        Bounds = bounds;
        StructuralToken = structuralToken;
    }

    /// <summary>Composes this node's <c>SKImageFilter</c> over the <paramref name="inner"/> upstream filter. Called at execution time.</summary>
    public Func<SKImageFilter?, SKImageFilter?> Factory { get; }

    /// <summary>Identity of the filter <em>kind</em> for the structural key.</summary>
    public object StructuralToken { get; }

    /// <inheritdoc/>
    public override bool IsCoordinateInvariant => false;

    /// <inheritdoc/>
    public override BoundsContract Bounds { get; }

    /// <inheritdoc/>
    public override EffectNodeKind Kind => EffectNodeKind.SkiaFilter;

    /// <summary>
    /// Builds a Skia-filter node from a filter-composing factory and its bounds contract. The backward bounds
    /// SHOULD cover the region the filter samples (a blur reads its inflation radius); when unknown, pass a
    /// render-time contract and the compiler falls back to the full input bounds. <paramref name="structuralToken"/>
    /// defaults to the factory's method identity.
    /// </summary>
    public static SkiaFilterNodeDescriptor Create(
        Func<SKImageFilter?, SKImageFilter?> factory, BoundsContract bounds, object? structuralToken = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new SkiaFilterNodeDescriptor(factory, bounds, structuralToken ?? factory.Method.MethodHandle.Value);
    }
}
