namespace Beutl.Graphics.Rendering;

public sealed class TargetCaptureDescription
{
    private TargetCaptureDescription(
        TargetRegion sourceRegion,
        Rect bounds,
        RenderHitTestContract hitTest,
        TargetCaptureScaleContract scale,
        IReadOnlyList<RenderResourceBinding> resources)
    {
        SourceRegion = sourceRegion;
        Bounds = bounds;
        HitTest = hitTest;
        Scale = scale;
        Resources = resources;
    }

    public TargetRegion SourceRegion { get; }

    public Rect Bounds { get; }

    public RenderHitTestContract HitTest { get; }

    public TargetCaptureScaleContract Scale { get; }

    /// <summary>Gets the resources the hit-test contract resolves its slots against.</summary>
    /// <remarks>
    /// A capture runs no callback of its own, so these are read only by
    /// <see cref="RenderHitTestContract.FromSlot{T}(RenderResourceSlot{T}, Func{T, Point, bool})"/>. A slot the
    /// contract names and this list does not bind has nothing to resolve against and fails the hit test.
    /// </remarks>
    public IReadOnlyList<RenderResourceBinding> Resources { get; }

    public static TargetCaptureDescription Create(
        TargetRegion sourceRegion,
        Rect bounds,
        RenderHitTestContract hitTest,
        TargetCaptureScaleContract scale,
        IReadOnlyList<RenderResourceBinding>? resources = null)
    {
        sourceRegion.ThrowIfUninitialized(nameof(sourceRegion));
        if (sourceRegion.Kind == TargetRegionKind.Empty)
            throw new ArgumentException("A target capture source region cannot be empty.", nameof(sourceRegion));

        RenderDescriptionValidation.ThrowUnlessFiniteNonEmpty(bounds, nameof(bounds));
        if (sourceRegion.Kind == TargetRegionKind.Region
            && !sourceRegion.Value.Contains(bounds))
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

        return new TargetCaptureDescription(
            sourceRegion,
            bounds,
            hitTest,
            scale,
            RenderDescriptionValidation.CopyResourceBindings(resources, nameof(resources)));
    }

    /// <summary>Checks only that the resolved region and domain are finite and non-empty.</summary>
    /// <remarks>A capture reaching past the pixels available to it reads transparent there.</remarks>
    internal void ValidateResolvedBounds(Rect resolvedSourceRegion, Rect targetDomain)
    {
        RenderDescriptionValidation.ThrowUnlessFiniteNonEmpty(resolvedSourceRegion, nameof(resolvedSourceRegion));
        RenderDescriptionValidation.ThrowUnlessFiniteNonEmpty(targetDomain, nameof(targetDomain));
    }
}
