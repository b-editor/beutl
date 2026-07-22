using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class MaterializedInputDescription
{
    private MaterializedInputDescription(
        RenderResource<RenderTarget> target,
        Rect bounds,
        EffectiveScale effectiveScale,
        RenderHitTestContract hitTest)
    {
        Target = target;
        Bounds = bounds;
        EffectiveScale = effectiveScale;
        HitTest = hitTest;
        DeviceBounds = PixelRect.FromRect(bounds, effectiveScale.Value);
    }

    public Rect Bounds { get; }

    public EffectiveScale EffectiveScale { get; }

    internal RenderResource<RenderTarget> Target { get; }

    internal RenderHitTestContract HitTest { get; }

    internal PixelRect DeviceBounds { get; }

    public static MaterializedInputDescription FromRenderTarget(
        RenderResource<RenderTarget> target,
        Rect bounds,
        EffectiveScale effectiveScale,
        RenderHitTestContract hitTest)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.RegistrationState == RenderResourceRegistrationState.Released)
            throw new ArgumentException("A released render-target resource cannot be materialized.", nameof(target));

        RenderDescriptionValidation.ThrowIfFiniteNonEmpty(bounds, nameof(bounds));
        if (effectiveScale.IsUnbounded)
        {
            throw new ArgumentException(
                "A materialized input requires a concrete positive effective scale.",
                nameof(effectiveScale));
        }

        hitTest.ThrowIfUninitialized(nameof(hitTest));
        if (hitTest.Kind == RenderHitTestContractKind.AnyInput)
        {
            throw new ArgumentException(
                "A materialized source has no logical inputs and cannot use AnyInput hit testing.",
                nameof(hitTest));
        }

        PixelRect deviceBounds = PixelRect.FromRect(bounds, effectiveScale.Value);
        if (deviceBounds.Width <= 0 || deviceBounds.Height <= 0)
        {
            throw new ArgumentException(
                "A materialized input must resolve to a non-empty device allocation.",
                nameof(bounds));
        }

        return new MaterializedInputDescription(target, bounds, effectiveScale, hitTest);
    }

    internal void ValidateTargetDeviceSize(RenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Width != DeviceBounds.Width || target.Height != DeviceBounds.Height)
        {
            throw new ArgumentException(
                "The render target device size must exactly match the materialized input's canonical device bounds.",
                nameof(target));
        }
    }
}
