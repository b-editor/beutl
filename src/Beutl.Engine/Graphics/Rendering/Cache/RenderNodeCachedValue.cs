using Beutl.Media;

namespace Beutl.Graphics.Rendering.Cache;

internal sealed record RenderNodeCachedValue
{
    public RenderNodeCachedValue(
        RenderTarget target,
        Rect bounds,
        EffectiveScale effectiveScale)
        : this(
            target,
            bounds,
            effectiveScale,
            CreateDeviceBounds(target, bounds, effectiveScale))
    {
    }

    public RenderNodeCachedValue(
        RenderTarget target,
        Rect bounds,
        EffectiveScale effectiveScale,
        PixelRect deviceBounds,
        Vector deviceGridOffset = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!RenderRectValidation.IsFiniteNonNegative(bounds))
            throw new ArgumentException("Cached value bounds must be finite and non-negative.", nameof(bounds));
        if (effectiveScale.IsUnbounded)
            throw new ArgumentException("A cached value requires a concrete density.", nameof(effectiveScale));
        if (deviceBounds.Width < 0 || deviceBounds.Height < 0)
            throw new ArgumentException("Cached value device bounds cannot have negative dimensions.", nameof(deviceBounds));
        if (deviceBounds.Size != new PixelSize(target.Width, target.Height))
        {
            throw new ArgumentException(
                "Cached value device bounds must match the backing target size.",
                nameof(deviceBounds));
        }
        PixelRect semanticDeviceBounds = PixelRect.FromRect(
            bounds.Translate(deviceGridOffset),
            effectiveScale.Value);
        if (deviceBounds.X > semanticDeviceBounds.X
            || deviceBounds.Y > semanticDeviceBounds.Y
            || deviceBounds.Right < semanticDeviceBounds.Right
            || deviceBounds.Bottom < semanticDeviceBounds.Bottom)
        {
            throw new ArgumentException(
                "Cached value device bounds must contain its semantic bounds.",
                nameof(deviceBounds));
        }

        Target = target;
        Bounds = bounds;
        CompleteBounds = bounds;
        EffectiveScale = effectiveScale;
        DeviceBounds = deviceBounds;
        DeviceGridOffset = deviceGridOffset;
    }

    public RenderTarget Target { get; }

    public Rect Bounds { get; }

    public Rect CompleteBounds { get; init; }

    public EffectiveScale EffectiveScale { get; }

    public PixelRect DeviceBounds { get; }

    public Vector DeviceGridOffset { get; }

    public Rect RasterBounds
        => DeviceBounds
            .ToRect(EffectiveScale.Value)
            .Translate(-DeviceGridOffset);

    private static PixelRect CreateDeviceBounds(
        RenderTarget target,
        Rect bounds,
        EffectiveScale effectiveScale)
    {
        ArgumentNullException.ThrowIfNull(target);
        PixelRect canonical = PixelRect.FromRect(bounds, effectiveScale.Value);
        return new PixelRect(canonical.Position, new PixelSize(target.Width, target.Height));
    }
}
