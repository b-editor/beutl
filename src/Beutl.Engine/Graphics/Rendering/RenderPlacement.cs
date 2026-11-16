using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>Places logical content and its backing pixels on the composition device grid.</summary>
public readonly record struct RenderPlacement
{
    /// <summary>Rounds the logical bounds outwards on the given device grid.</summary>
    public RenderPlacement(Rect logicalBounds, EffectiveScale scale, Vector deviceGridOffset = default)
        : this(logicalBounds, scale, PixelRect.FromRect(logicalBounds.Translate(deviceGridOffset), scale.Value), deviceGridOffset)
    {
    }

    /// <summary>Describes an explicit device footprint, which may include padding around the content.</summary>
    public RenderPlacement(Rect logicalBounds, EffectiveScale scale, PixelRect deviceBounds, Vector deviceGridOffset)
        : this(logicalBounds, scale, deviceBounds, deviceGridOffset, deviceBounds.Position.ToPoint(scale.Value) - deviceGridOffset)
    {
        if (scale.IsUnbounded)
            throw new ArgumentException("A raster placement requires a concrete density.", nameof(scale));
        if (deviceBounds.Width < 0 || deviceBounds.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(deviceBounds));
        if (!float.IsFinite(deviceGridOffset.X) || !float.IsFinite(deviceGridOffset.Y))
            throw new ArgumentException("The device-grid offset must be finite.", nameof(deviceGridOffset));
    }

    private RenderPlacement(Rect logicalBounds, EffectiveScale scale, PixelRect deviceBounds, Vector deviceGridOffset, Point logicalOrigin)
    {
        LogicalBounds = logicalBounds;
        Scale = scale;
        DeviceBounds = deviceBounds;
        DeviceGridOffset = deviceGridOffset;
        LogicalOrigin = logicalOrigin;
    }

    public Rect LogicalBounds { get; }
    public EffectiveScale Scale { get; }
    public PixelRect DeviceBounds { get; }

    /// <summary>The translation in logical units from local coordinates to the composition device grid.</summary>
    public Vector DeviceGridOffset { get; }

    /// <summary>The local point corresponding to the top-left corner of the backing pixels.</summary>
    public Point LogicalOrigin { get; }
    public PixelSize DeviceSize => DeviceBounds.Size;
    public Rect RasterBounds => new(LogicalOrigin, DeviceSize.ToSize(Scale.Value));

    /// <summary>Converts an absolute composition-device point to local logical coordinates.</summary>
    public Point ToLogical(PixelPoint devicePoint)
        => new((float)(LogicalOrigin.X + ((double)devicePoint.X - DeviceBounds.X) / Scale.Value),
            (float)(LogicalOrigin.Y + ((double)devicePoint.Y - DeviceBounds.Y) / Scale.Value));

    /// <summary>Returns the smallest composition-device rectangle covering a local logical rectangle.</summary>
    public PixelRect ToDevice(Rect logical) => PixelRect.FromRect(logical.Translate(DeviceGridOffset), Scale.Value);

    internal static RenderPlacement FromRasterOrigin(Rect bounds, EffectiveScale scale, PixelRect deviceBounds, Point origin)
        => new(bounds, scale, deviceBounds, deviceBounds.Position.ToPoint(scale.Value) - origin, origin);

    internal RenderPlacement WithLogicalBounds(Rect bounds)
        => new(bounds, Scale, DeviceBounds, DeviceGridOffset, LogicalOrigin);

    internal RenderPlacement MoveContent(Rect bounds)
    {
        Vector delta = bounds.Position - LogicalBounds.Position;
        return new(bounds, Scale, DeviceBounds, DeviceGridOffset - delta, LogicalOrigin + delta);
    }
}
