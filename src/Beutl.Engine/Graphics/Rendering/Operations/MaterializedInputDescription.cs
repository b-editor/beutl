using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class MaterializedInputDescription
{
    private MaterializedInputDescription(
        RenderResource<RenderTarget> target,
        Rect bounds,
        EffectiveScale effectiveScale,
        PixelRect deviceBounds,
        Vector deviceGridOffset,
        RenderHitTestContract hitTest)
    {
        Target = target;
        Bounds = bounds;
        EffectiveScale = effectiveScale;
        DeviceBounds = deviceBounds;
        DeviceGridOffset = deviceGridOffset;
        HitTest = hitTest;
    }

    public Rect Bounds { get; }

    public EffectiveScale EffectiveScale { get; }

    public PixelRect DeviceBounds { get; }

    public Vector DeviceGridOffset { get; }

    public Rect RasterBounds => DeviceBounds
        .ToRect(EffectiveScale.Value)
        .Translate(-DeviceGridOffset);

    internal RenderResource<RenderTarget> Target { get; }

    internal RenderHitTestContract HitTest { get; }

    public static MaterializedInputDescription FromRenderTarget(
        RenderResource<RenderTarget> target,
        Rect bounds,
        EffectiveScale effectiveScale,
        PixelRect deviceBounds,
        Vector deviceGridOffset,
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

        if (deviceBounds.Width <= 0 || deviceBounds.Height <= 0)
        {
            throw new ArgumentException(
                "A materialized input must resolve to a non-empty device allocation.",
                nameof(deviceBounds));
        }
        if (!float.IsFinite(deviceGridOffset.X) || !float.IsFinite(deviceGridOffset.Y))
            throw new ArgumentException("A materialized input requires a finite device-grid offset.", nameof(deviceGridOffset));

        Rect rasterBounds = deviceBounds
            .ToRect(effectiveScale.Value)
            .Translate(-deviceGridOffset);
        if (!RenderDescriptionValidation.Contains(rasterBounds, bounds))
        {
            throw new ArgumentException(
                "The materialized input's physical footprint must contain its semantic bounds on the declared device grid.",
                nameof(deviceBounds));
        }

        return new MaterializedInputDescription(
            target,
            bounds,
            effectiveScale,
            deviceBounds,
            deviceGridOffset,
            hitTest);
    }

    internal void ValidateTargetDeviceSize(RenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Width != DeviceBounds.Width || target.Height != DeviceBounds.Height)
        {
            throw new ArgumentException(
                "The render target device size must exactly match the materialized input's declared device bounds.",
                nameof(target));
        }
    }
}
