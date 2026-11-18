using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

public sealed class EffectTarget : IDisposable
{
    private object? _target;
    private readonly RenderPlacement _allocation;

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
        : this(
            new RenderPlacement(originalBounds, scale, deviceBounds, deviceGridOffset),
            CopyValidatedBacking(renderTarget, scale, deviceBounds),
            preserveImperativeRasterPlacement)
    {
    }

    private EffectTarget(
        EffectTargetRenderTargetLease renderTargetLease,
        Rect originalBounds,
        EffectiveScale scale,
        PixelRect deviceBounds,
        Vector deviceGridOffset,
        bool preserveImperativeRasterPlacement)
        : this(
            new RenderPlacement(originalBounds, scale, deviceBounds, deviceGridOffset),
            ValidatedBacking(renderTargetLease, scale, deviceBounds),
            preserveImperativeRasterPlacement)
    {
    }

    /// <summary>Takes ownership of a validated backing: a render-target reference or a pooled lease.</summary>
    private EffectTarget(
        RenderPlacement allocation,
        object backing,
        bool preserveImperativeRasterPlacement)
    {
        _target = backing;
        _allocation = allocation;
        OriginalBounds = allocation.LogicalBounds;
        Bounds = allocation.LogicalBounds;
        PreserveImperativeRasterPlacement = preserveImperativeRasterPlacement;
    }

    /// <summary>Validates a render-target backing and returns the reference the new target owns.</summary>
    private static object CopyValidatedBacking(RenderTarget renderTarget, EffectiveScale scale, PixelRect deviceBounds)
    {
        ArgumentNullException.ThrowIfNull(renderTarget);
        ValidateBacking(renderTarget.Width, renderTarget.Height, scale, deviceBounds);
        return renderTarget.ShallowCopy();
    }

    /// <summary>Validates a pooled-lease backing, which the new target takes over as it is.</summary>
    private static object ValidatedBacking(
        EffectTargetRenderTargetLease renderTargetLease,
        EffectiveScale scale,
        PixelRect deviceBounds)
    {
        ArgumentNullException.ThrowIfNull(renderTargetLease);
        RenderTarget backing = renderTargetLease.Target;
        ValidateBacking(backing.Width, backing.Height, scale, deviceBounds);
        return renderTargetLease;
    }

    private static void ValidateBacking(int width, int height, EffectiveScale scale, PixelRect deviceBounds)
    {
        if (scale.IsUnbounded)
            throw new ArgumentException("An effect target requires a concrete density.", nameof(scale));
        if (deviceBounds.Size != new PixelSize(width, height))
        {
            throw new ArgumentException(
                "Effect target device bounds must match the backing target size.",
                nameof(deviceBounds));
        }
    }

    public EffectTarget()
    {
    }

    public Rect OriginalBounds { get; set; }

    public Rect Bounds { get; set; }

    /// <summary>
    /// Gets or sets the bounds of all of the value this target holds a part of, or <see langword="null"/> when it holds
    /// all of it and <see cref="Bounds"/> are its complete bounds.
    /// </summary>
    /// <remarks>
    /// A request can hand an effect only the part of a value it needs. What reads the result can still depend on the
    /// value's complete extent, as a script's width and height do, so the executor moves these bounds through each
    /// effect as the effect moves <see cref="Bounds"/>.
    /// </remarks>
    internal Rect? CompleteBounds { get; set; }

    /// <summary>The current content and raster footprint. Moving Bounds translates the raster on its original allocation.</summary>
    public RenderPlacement Placement => _allocation.MoveContent(Bounds);

    internal RenderPlacement Allocation => _allocation;

    internal bool PreserveImperativeRasterPlacement { get; }

    /// <summary>
    /// Gets or sets where the content lies, when a flush laid it on whole device pixels for a callback that keeps it in
    /// place; <see langword="null"/> when <see cref="Bounds"/> are the content's own.
    /// </summary>
    /// <remarks>
    /// Such a callback maps what it lays over the content, such as a brush, onto these bounds, and sets them on each
    /// target it makes from this one. The target goes on with them after the callback.
    /// </remarks>
    internal Rect? ContentBounds { get; set; }

    // Where Draw puts the raster: at Placement.RasterBounds, or at Bounds for a target that keeps an imperative placement.
    internal Rect DrawnBounds
        => RenderTarget is null
            ? Rect.Empty
            : PreserveImperativeRasterPlacement
                ? new Rect(Bounds.Position, Placement.RasterBounds.Size)
                : Placement.RasterBounds;

    public RenderTarget? RenderTarget => _target switch
    {
        RenderTarget renderTarget => renderTarget,
        EffectTargetRenderTargetLease renderTargetLease => renderTargetLease.Target,
        _ => null,
    };

    public bool IsEmpty => _target == null;

    /// <summary>Whether this target has a backing render target and a concrete scale to rasterize at.</summary>
    internal bool IsMaterialized => RenderTarget is not null && !_allocation.Scale.IsUnbounded;

    /// <summary>The <see cref="EffectTargets"/> that currently owns this target, if any.</summary>
    internal EffectTargets? Owner { get; set; }

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
            return new EffectTarget { OriginalBounds = OriginalBounds, Bounds = Bounds, CompleteBounds = CompleteBounds };
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
        => CreateReplacementCore(CopyValidatedBacking(renderTarget, _allocation.Scale, _allocation.DeviceBounds));

    internal EffectTarget CreateReplacement(RenderTargetLease renderTargetLease)
        => CreateReplacement(new EffectTargetRenderTargetLease(renderTargetLease));

    private EffectTarget CreateReplacement(EffectTargetRenderTargetLease renderTargetLease)
        => CreateReplacementCore(ValidatedBacking(renderTargetLease, _allocation.Scale, _allocation.DeviceBounds));

    /// <summary>Wraps a validated backing as a target with this target's footprint and bounds.</summary>
    private EffectTarget CreateReplacementCore(object backing)
    {
        return new EffectTarget(
            _allocation,
            backing,
            PreserveImperativeRasterPlacement)
        {
            Bounds = Bounds,
            OriginalBounds = OriginalBounds,
            CompleteBounds = CompleteBounds,
        };
    }

    /// <summary>
    /// Moves this target's raster into a target that draws it where it is and takes <paramref name="bounds"/> as its
    /// bounds. This target is left empty.
    /// </summary>
    /// <remarks>
    /// The raster must lie at its allocated place, which <see cref="RenderPlacement.RasterBounds"/> leaves only when the bounds move.
    /// The new target does not keep an imperative placement, so its bounds can be narrower than the raster.
    /// </remarks>
    internal EffectTarget TakeRasterWithBounds(Rect bounds)
    {
        object backing = _target ?? throw new InvalidOperationException("An empty target has no raster to move.");
        var result = new EffectTarget(
            _allocation.WithLogicalBounds(bounds),
            backing,
            preserveImperativeRasterPlacement: false)
        {
            CompleteBounds = CompleteBounds,
        };
        _target = null;
        return result;
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
            Rect rasterBounds = Placement.RasterBounds;
            Point localOrigin = PreserveImperativeRasterPlacement
                ? default
                : rasterBounds.Position - Bounds.Position;
            // Draw the complete backing footprint. Bounds is semantic metadata and can be
            // translated or inflated independently, so it must never be used as the image size.
            // A point blit samples nearest, so it only reproduces the buffer when the destination
            // lands on exact device pixels; a filter chain anchored at a fractional frame offset
            // does not, and must resample instead of snapping the content to the grid.
            var destination = new Rect(localOrigin, rasterBounds.Size);
            if ((_allocation.Scale.IsUnbounded || _allocation.Scale.Value == 1f)
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
