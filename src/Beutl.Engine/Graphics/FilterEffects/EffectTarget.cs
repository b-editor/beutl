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
                scale.IsUnbounded ? EffectiveScale.At(1f) : scale),
            CreateEffectItemDeviceGridOffset(
                originalBounds,
                scale.IsUnbounded ? EffectiveScale.At(1f) : scale),
            preserveImperativeRasterPlacement: true)
    {
    }

    internal EffectTarget(
        RenderTarget renderTarget,
        Rect originalBounds,
        EffectiveScale scale,
        PixelRect deviceBounds,
        Vector deviceGridOffset = default,
        bool preserveImperativeRasterPlacement = false)
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
        _allocationRasterBounds = deviceBounds
            .ToRect(scale.Value)
            .Translate(-deviceGridOffset);
        OriginalBounds = originalBounds;
        Bounds = originalBounds;
        Scale = scale;
        DeviceBounds = deviceBounds;
        DeviceGridOffset = deviceGridOffset;
        PreserveImperativeRasterPlacement = preserveImperativeRasterPlacement;
    }

    private EffectTarget(
        EffectTargetRenderTargetLease renderTargetLease,
        Rect originalBounds,
        EffectiveScale scale,
        PixelRect deviceBounds,
        Vector deviceGridOffset,
        bool preserveImperativeRasterPlacement)
    {
        ArgumentNullException.ThrowIfNull(renderTargetLease);
        if (scale.IsUnbounded)
            throw new ArgumentException("An effect target requires a concrete density.", nameof(scale));
        if (deviceBounds.Size != new PixelSize(
                renderTargetLease.Target.Width,
                renderTargetLease.Target.Height))
        {
            throw new ArgumentException(
                "Effect target device bounds must match the backing target size.",
                nameof(deviceBounds));
        }

        _target = renderTargetLease;
        _allocationBounds = originalBounds;
        _allocationRasterBounds = deviceBounds
            .ToRect(scale.Value)
            .Translate(-deviceGridOffset);
        OriginalBounds = originalBounds;
        Bounds = originalBounds;
        Scale = scale;
        DeviceBounds = deviceBounds;
        DeviceGridOffset = deviceGridOffset;
        PreserveImperativeRasterPlacement = preserveImperativeRasterPlacement;
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
    /// Gets the immutable composition-device footprint used to allocate the backing target.
    /// </summary>
    /// <remarks>
    /// Convert this footprint to effect-local coordinates with
    /// <c>DeviceBounds.ToRect(Scale.Value).Translate(-DeviceGridOffset)</c>.
    /// </remarks>
    public PixelRect DeviceBounds { get; }

    /// <summary>
    /// Gets the translation from effect-local logical coordinates to the composition-device grid
    /// used to round the backing target.
    /// </summary>
    public Vector DeviceGridOffset { get; }

    internal bool PreserveImperativeRasterPlacement { get; }

    /// <summary>
    /// Gets the current effect-local, pixel-aligned logical footprint. Moving <see cref="Bounds"/>
    /// translates this footprint without stretching the backing pixels.
    /// </summary>
    public Rect RasterBounds
        => _allocationRasterBounds.Translate(Bounds.Position - _allocationBounds.Position);

    public RenderTarget? RenderTarget => _target switch
    {
        RenderTarget renderTarget => renderTarget,
        EffectTargetRenderTargetLease renderTargetLease => renderTargetLease.Target,
        _ => null,
    };

    public bool IsEmpty => _target == null;

    public EffectTarget Clone()
    {
        if (_target is EffectTargetRenderTargetLease renderTargetLease)
        {
            return CreateReplacement(renderTargetLease.Retain());
        }
        else if (RenderTarget != null)
        {
            return CreateReplacement(RenderTarget);
        }
        else
        {
            return this;
        }
    }

    /// <summary>
    /// Wraps a freshly acquired pooled lease as a target, so a path that allocates its own surfaces can honour
    /// the caller's <see cref="IRenderTargetFactory"/> without also taking over the lease's lifetime.
    /// </summary>
    internal static EffectTarget FromLease(
        RenderTargetLease renderTargetLease,
        Rect originalBounds,
        EffectiveScale scale,
        PixelRect deviceBounds,
        Vector deviceGridOffset = default,
        bool preserveImperativeRasterPlacement = false)
    {
        ArgumentNullException.ThrowIfNull(renderTargetLease);
        return new EffectTarget(
            new EffectTargetRenderTargetLease(renderTargetLease),
            originalBounds,
            scale,
            deviceBounds,
            deviceGridOffset,
            preserveImperativeRasterPlacement);
    }

    internal EffectTarget CreateReplacement(RenderTarget renderTarget)
    {
        return new EffectTarget(
            renderTarget,
            _allocationBounds,
            Scale,
            DeviceBounds,
            DeviceGridOffset,
            PreserveImperativeRasterPlacement)
        {
            Bounds = Bounds,
            OriginalBounds = OriginalBounds,
        };
    }

    internal EffectTarget CreateReplacement(RenderTargetLease renderTargetLease)
        => CreateReplacement(new EffectTargetRenderTargetLease(renderTargetLease));

    private EffectTarget CreateReplacement(EffectTargetRenderTargetLease renderTargetLease)
    {
        return new EffectTarget(
            renderTargetLease,
            _allocationBounds,
            Scale,
            DeviceBounds,
            DeviceGridOffset,
            PreserveImperativeRasterPlacement)
        {
            Bounds = Bounds,
            OriginalBounds = OriginalBounds,
        };
    }

    internal EffectTargetRenderTargetLease? TakeRenderTargetLease()
    {
        if (_target is not EffectTargetRenderTargetLease renderTargetLease)
            return null;

        _target = null;
        return renderTargetLease;
    }

    public void Dispose()
    {
        switch (_target)
        {
            case RenderTarget renderTarget:
                renderTarget.Dispose();
                break;
            case EffectTargetRenderTargetLease renderTargetLease:
                renderTargetLease.Dispose();
                break;
        }

        _target = null;
        OriginalBounds = default;
    }

    public void Draw(ImmediateCanvas canvas)
    {
        if (RenderTarget != null)
        {
            Rect rasterBounds = RasterBounds;
            Point localOrigin = PreserveImperativeRasterPlacement
                ? default
                : rasterBounds.Position - Bounds.Position;
            // Draw the complete backing footprint. Bounds is semantic metadata and can be
            // translated or inflated independently, so it must never be used as the image size.
            // A point blit samples nearest, so it only reproduces the buffer when the destination
            // lands on exact device pixels; a filter chain anchored at a fractional frame offset
            // does not, and must resample instead of snapping the content to the grid.
            var destination = new Rect(localOrigin, rasterBounds.Size);
            if ((Scale.IsUnbounded || Scale.Value == 1f)
                && canvas.Density == 1f
                && canvas.CanBlitLossless(destination, new PixelSize(RenderTarget.Width, RenderTarget.Height)))
            {
                canvas.DrawRenderTarget(RenderTarget, localOrigin);
            }
            else
            {
                canvas.DrawRenderTargetScaled(RenderTarget, destination);
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

    private static Vector CreateEffectItemDeviceGridOffset(Rect bounds, EffectiveScale scale)
    {
        Point deviceOrigin = PixelRect.FromRect(bounds, scale.Value)
            .ToRect(scale.Value)
            .Position;
        return deviceOrigin - bounds.Position;
    }
}
