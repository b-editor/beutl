namespace Beutl.Graphics.Rendering;

public sealed class TargetCaptureDescription
{
    private TargetCaptureDescription(
        TargetRegion sourceRegion,
        Rect bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale)
    {
        SourceRegion = sourceRegion;
        Bounds = bounds;
        HitTest = hitTest;
        Scale = scale;
    }

    public TargetRegion SourceRegion { get; }

    public Rect Bounds { get; }

    public RenderHitTestContract HitTest { get; }

    public RenderScaleContract Scale { get; }

    public static TargetCaptureDescription Create(
        TargetRegion sourceRegion,
        Rect bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale)
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
        if (scale.Kind is not (RenderScaleContractKind.MaterializeAtWorkingScale or RenderScaleContractKind.Custom))
        {
            throw new ArgumentException(
                "A public target capture requires MaterializeAtWorkingScale or a Custom scale contract.",
                nameof(scale));
        }

        return new TargetCaptureDescription(sourceRegion, bounds, hitTest, scale);
    }

    internal void ValidateResolvedBounds(Rect resolvedSourceRegion, Rect targetDomain)
    {
        RenderDescriptionValidation.ThrowIfFiniteNonEmpty(resolvedSourceRegion, nameof(resolvedSourceRegion));
        RenderDescriptionValidation.ThrowIfFiniteNonEmpty(targetDomain, nameof(targetDomain));
        if (!RenderDescriptionValidation.Contains(resolvedSourceRegion, Bounds)
            || !RenderDescriptionValidation.Contains(targetDomain, Bounds))
        {
            throw new InvalidOperationException(
                "Target capture bounds must be contained by both the resolved source region and current target domain.");
        }
    }
}
