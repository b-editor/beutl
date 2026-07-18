using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// An <c>SKImageFilter</c> node (feature 004, data-model §1): blur, drop-shadow, morphology, matrix transform,
/// convolution, etc. Adjacent Skia-filter nodes group into one filtered draw (C2), reproducing today's
/// legacy image-filter accumulation as an explicit plan pass. Non-invariant: it carries a mandatory
/// <see cref="BoundsContract"/>. The <see cref="Factory"/> composes the frame's filter over the upstream filter
/// (<see langword="null"/> when this node heads the group); its <see cref="StructuralToken"/> identifies the
/// filter kind for the structural key, leaving the parameters (sigma, offset, kernel) as re-resolved sizes (A4).
/// </summary>
public sealed record SkiaFilterNodeDescriptor : EffectNodeDescriptor
{
    internal override EffectNodeKind Kind => EffectNodeKind.SkiaFilter;

    private SkiaFilterNodeDescriptor(
        Func<SKImageFilter?, SKImageFilter?> factory, BoundsContract bounds, object structuralToken)
    {
        Factory = factory;
        Bounds = bounds;
        StructuralToken = structuralToken;
    }

    /// <summary>
    /// Composes this node over a borrowed <paramref name="inner"/> filter. The callback must not dispose or retain
    /// <paramref name="inner"/>. Returning null or the same instance is identity; a different result transfers one
    /// fresh, independently disposable owned reference to the executor. Execution may invoke the factory more than
    /// once per frame after fan-out, so cached results are invalid.
    /// </summary>
    public Func<SKImageFilter?, SKImageFilter?> Factory { get; }

    /// <summary>Identity of the filter <em>kind</em> for the structural key. Tokens share a plan only when their
    /// runtime types and <see cref="object.Equals(object?)"/> values match; equality and hash code must stay stable.</summary>
    public object StructuralToken { get; }

    /// <inheritdoc/>
    public override bool IsCoordinateInvariant => false;

    /// <inheritdoc/>
    public override BoundsContract Bounds { get; }

    /// <summary>
    /// Builds a Skia-filter node from a filter-composing factory and its bounds contract. The backward bounds
    /// SHOULD cover the region the filter samples (a blur reads its inflation radius); when unknown, pass
    /// <see cref="BoundsContract.FullFrame"/> so the compiler uses the complete input. <paramref name="structuralToken"/>
    /// defaults to the factory's method identity. The factory ownership rules documented on <see cref="Factory"/>
    /// apply to every invocation.
    /// </summary>
    public static SkiaFilterNodeDescriptor Create(
        Func<SKImageFilter?, SKImageFilter?> factory, BoundsContract bounds, object? structuralToken = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        bounds.ThrowIfUninitialized(nameof(bounds));
        return new SkiaFilterNodeDescriptor(factory, bounds, structuralToken ?? factory.Method);
    }
}
