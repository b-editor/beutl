namespace Beutl.Graphics.Rendering;

public sealed class TargetCaptureDescription
{
    private TargetCaptureDescription(
        TargetRegion sourceRegion,
        Rect bounds,
        RenderHitTestContract hitTest,
        TargetCaptureScaleContract scale)
    {
        SourceRegion = sourceRegion;
        Bounds = bounds;
        HitTest = hitTest;
        Scale = scale;
    }

    public TargetRegion SourceRegion { get; }

    public Rect Bounds { get; }

    public RenderHitTestContract HitTest { get; }

    public TargetCaptureScaleContract Scale { get; }

    public static TargetCaptureDescription Create(
        TargetRegion sourceRegion,
        Rect bounds,
        RenderHitTestContract hitTest,
        TargetCaptureScaleContract scale)
    {
        sourceRegion.ThrowIfUninitialized(nameof(sourceRegion));
        if (sourceRegion.Kind == TargetRegionKind.Empty)
            throw new ArgumentException("A target capture source region cannot be empty.", nameof(sourceRegion));

        RenderDescriptionValidation.ThrowIfFiniteNonEmpty(bounds, nameof(bounds));
        if (sourceRegion.Kind == TargetRegionKind.Region
            && !RenderDescriptionValidation.Contains(sourceRegion.Value, bounds))
        {
            throw new ArgumentException(
                "Target capture bounds must be contained by a finite source region.",
                nameof(bounds));
        }

        hitTest.ThrowIfUninitialized(nameof(hitTest));
        if (hitTest.Kind == RenderHitTestContractKind.AnyInput)
        {
            throw new ArgumentException(
                "A target capture has no logical value inputs and cannot use AnyInput hit testing.",
                nameof(hitTest));
        }

        scale.ThrowIfUninitialized(nameof(scale));

        return new TargetCaptureDescription(sourceRegion, bounds, hitTest, scale);
    }

    /// <summary>
    /// Checks the region and domain a capture resolved against once the surrounding graph is known.
    /// </summary>
    /// <remarks>
    /// Both are decided by the scope the capture ends up in, not by the author, who has neither at the point
    /// they call <see cref="Create"/>. Requiring <see cref="Bounds"/> to sit inside them would therefore be a
    /// precondition nobody can satisfy: the same description fails or succeeds depending on where it is used.
    /// A capture reaching past the pixels available to it reads transparent there instead - the value is
    /// cleared before the copy - which is the same answer as capturing an area nothing has drawn into.
    /// The author-observable half of this rule is still enforced at <see cref="Create"/>: an explicit finite
    /// source region must contain the bounds asked of it.
    /// </remarks>
    internal void ValidateResolvedBounds(Rect resolvedSourceRegion, Rect targetDomain)
    {
        RenderDescriptionValidation.ThrowIfFiniteNonEmpty(resolvedSourceRegion, nameof(resolvedSourceRegion));
        RenderDescriptionValidation.ThrowIfFiniteNonEmpty(targetDomain, nameof(targetDomain));
    }
}
