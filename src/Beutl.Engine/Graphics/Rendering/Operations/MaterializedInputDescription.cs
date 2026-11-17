using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class MaterializedInputDescription
{
    private MaterializedInputDescription(
        RenderResource<RenderTarget> target,
        RenderPlacement placement,
        RenderHitTestContract hitTest,
        IReadOnlyList<RenderResourceBinding> resources)
    {
        Target = target;
        Placement = placement;
        HitTest = hitTest;
        Resources = resources;
    }

    public RenderPlacement Placement { get; }

    /// <summary>Gets the resources the hit-test contract resolves its slots against.</summary>
    /// <remarks>
    /// A materialized input runs no callback of its own, so these are read only by
    /// <see cref="RenderHitTestContract.FromSlot{T}(RenderResourceSlot{T}, Func{T, Point, bool})"/>. A slot the
    /// contract names and this list does not bind has nothing to resolve against and fails the hit test.
    /// </remarks>
    public IReadOnlyList<RenderResourceBinding> Resources { get; }

    internal RenderResource<RenderTarget> Target { get; }

    internal RenderHitTestContract HitTest { get; }

    public static MaterializedInputDescription FromRenderTarget(
        RenderResource<RenderTarget> target,
        RenderPlacement placement,
        RenderHitTestContract hitTest,
        IReadOnlyList<RenderResourceBinding>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.RegistrationState == RenderResourceRegistrationState.Released)
            throw new ArgumentException("A released render-target resource cannot be materialized.", nameof(target));

        RenderDescriptionValidation.ThrowUnlessFiniteNonEmpty(placement.LogicalBounds, nameof(placement));
        if (placement.Scale.IsUnbounded || !placement.RasterBounds.Contains(placement.LogicalBounds))
            throw new ArgumentException("A materialized input requires a raster covering its logical bounds.", nameof(placement));

        hitTest.ThrowIfUninitialized(nameof(hitTest));
        if (hitTest.Kind == RenderHitTestContractKind.AnyInput)
        {
            throw new ArgumentException(
                "A materialized source has no logical inputs and cannot use AnyInput hit testing.",
                nameof(hitTest));
        }

        return new MaterializedInputDescription(
            target,
            placement,
            hitTest,
            RenderDescriptionValidation.CopyResourceBindings(resources, nameof(resources)));
    }

    internal void ValidateTargetDeviceSize(RenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Width != Placement.DeviceBounds.Width || target.Height != Placement.DeviceBounds.Height)
        {
            throw new ArgumentException(
                "The render target device size must exactly match the materialized input's declared device bounds.",
                nameof(target));
        }
    }
}
