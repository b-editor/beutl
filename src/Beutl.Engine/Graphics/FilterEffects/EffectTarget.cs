using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

public sealed class EffectTarget : IDisposable
{
    private object? _target;
    private readonly Rect _allocationBounds;
    private readonly Rect _allocationRasterBounds;

    public EffectTarget(RenderTarget renderTarget, Rect originalBounds, EffectiveScale scale = default)
        : this(
            renderTarget,
            originalBounds,
            scale.IsUnbounded ? EffectiveScale.At(1f) : scale,
            CreateDeviceBounds(
                renderTarget,
                originalBounds,
                scale.IsUnbounded ? EffectiveScale.At(1f) : scale))
    {
    }

    internal EffectTarget(
        RenderTarget renderTarget,
        Rect originalBounds,
        EffectiveScale scale,
        PixelRect deviceBounds)
    {
        ArgumentNullException.ThrowIfNull(renderTarget);
        if (scale.IsUnbounded)
            throw new ArgumentException("An effect target requires a concrete density.", nameof(scale));
        if (deviceBounds.Size != new PixelSize(renderTarget.Width, renderTarget.Height))
        {
            throw new ArgumentException(
                "Effect target device bounds must match the backing target size.",
                nameof(deviceBounds));
        }

        _target = renderTarget.ShallowCopy();
        _allocationBounds = originalBounds;
        _allocationRasterBounds = deviceBounds.ToRect(scale.Value);
        OriginalBounds = originalBounds;
        Bounds = originalBounds;
        Scale = scale;
        DeviceBounds = deviceBounds;
    }

    public EffectTarget()
    {
    }

    public Rect OriginalBounds { get; set; }

    public Rect Bounds { get; set; }

    /// <summary>
    /// Supply density: <see cref="EffectiveScale.Unbounded"/> for vector, concrete <see cref="EffectiveScale.At"/> for rasterized buffers.
    /// </summary>
    public EffectiveScale Scale { get; init; }

    /// <summary>
    /// Gets the immutable device-pixel footprint used to allocate the backing target.
    /// </summary>
    public PixelRect DeviceBounds { get; }

    /// <summary>
    /// Gets the current pixel-aligned logical footprint. Moving <see cref="Bounds"/> translates
    /// this footprint without stretching the backing pixels.
    /// </summary>
    public Rect RasterBounds
        => _allocationRasterBounds.Translate(Bounds.Position - _allocationBounds.Position);

    public RenderTarget? RenderTarget => _target as RenderTarget;

    public bool IsEmpty => _target == null;

    public EffectTarget Clone()
    {
        if (RenderTarget != null)
        {
            return CreateReplacement(RenderTarget);
        }
        else
        {
            return this;
        }
    }

    internal EffectTarget CreateReplacement(RenderTarget renderTarget)
    {
        return new EffectTarget(renderTarget, _allocationBounds, Scale, DeviceBounds)
        {
            Bounds = Bounds,
            OriginalBounds = OriginalBounds,
        };
    }

    public void Dispose()
    {
        RenderTarget?.Dispose();
        _target = null;
        OriginalBounds = default;
    }

    public void Draw(ImmediateCanvas canvas)
    {
        if (RenderTarget != null)
        {
            Rect rasterBounds = RasterBounds;
            Point localOrigin = rasterBounds.Position - Bounds.Position;
            // Draw the complete backing footprint. Bounds is semantic metadata and can be
            // translated or inflated independently, so it must never be used as the image size.
            if ((Scale.IsUnbounded || Scale.Value == 1f) && canvas.Density == 1f)
            {
                canvas.DrawRenderTarget(RenderTarget, localOrigin);
            }
            else
            {
                canvas.DrawRenderTargetScaled(RenderTarget,
                    new Rect(localOrigin, rasterBounds.Size));
            }
        }
    }

    private static PixelRect CreateDeviceBounds(
        RenderTarget renderTarget,
        Rect bounds,
        EffectiveScale scale)
    {
        ArgumentNullException.ThrowIfNull(renderTarget);
        PixelRect canonical = PixelRect.FromRect(bounds, scale.Value);
        return new PixelRect(canonical.Position, new PixelSize(renderTarget.Width, renderTarget.Height));
    }
}
