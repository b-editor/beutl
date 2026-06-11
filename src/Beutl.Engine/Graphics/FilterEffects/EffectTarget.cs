using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

public sealed class EffectTarget : IDisposable
{
    private object? _target;

    public EffectTarget(RenderNodeOperation node)
    {
        _target = node;
        OriginalBounds = node.Bounds;
        Bounds = node.Bounds;
        Scale = node.EffectiveScale;
    }

    public EffectTarget(RenderTarget renderTarget, Rect originalBounds, EffectiveScale scale = default)
    {
        _target = renderTarget.ShallowCopy();
        OriginalBounds = originalBounds;
        Bounds = originalBounds;
        Scale = scale;
    }

    public EffectTarget()
    {
    }

    public Rect OriginalBounds { get; set; }

    public Rect Bounds { get; set; }

    /// <summary>
    /// The supply density of this target's backing pixels (feature 003). <see cref="EffectiveScale.Unbounded"/>
    /// for a vector <see cref="NodeOperation"/>; a concrete <see cref="EffectiveScale.At"/> for a flushed
    /// <see cref="RenderTarget"/> buffer rendered at a working scale. Drives mixed-scale reconciliation.
    /// </summary>
    public EffectiveScale Scale { get; set; }

    public RenderNodeOperation? NodeOperation => _target as RenderNodeOperation;

    public RenderTarget? RenderTarget => _target as RenderTarget;

    public bool IsEmpty => _target == null;

    public EffectTarget Clone()
    {
        if (RenderTarget != null)
        {
            return new EffectTarget(RenderTarget, OriginalBounds, Scale) { Bounds = Bounds };
        }
        else
        {
            return this;
        }
    }

    public void Dispose()
    {
        RenderTarget?.Dispose();
        NodeOperation?.Dispose();
        _target = null;
        OriginalBounds = default;
    }

    public void Draw(ImmediateCanvas canvas)
    {
        if (RenderTarget != null)
        {
            // feature 003: a buffer captured At(w) is ceil(footprint × w) device px; draw it into its OWN
            // LOGICAL footprint = pixel size ÷ w (origin-anchored, mirroring the point-blit at (0,0)) so the
            // ambient CTM maps it. The footprint is derived from the buffer, NOT OriginalBounds, because a
            // downstream filter (e.g. a blur/shadow wrapped in DelayAnimation) can inflate OriginalBounds while
            // the buffer still represents the original area — using OriginalBounds would stretch it. Unbounded /
            // unit-scale keeps the bare point blit ONLY on a density-1 canvas (byte-identical pre-feature path);
            // on a scaled canvas the point blit would be CTM-resampled with NEAREST sampling, so route it
            // through the Mitchell blit instead — same geometry, consistent kernel.
            if ((Scale.IsUnbounded || Scale.Value == 1f) && canvas.OutputScale == 1f)
            {
                canvas.DrawRenderTarget(RenderTarget, default);
            }
            else
            {
                float density = Scale.IsUnbounded ? 1f : Scale.Value;
                canvas.DrawRenderTargetScaled(RenderTarget,
                    new Rect(0, 0, RenderTarget.Width / density, RenderTarget.Height / density));
            }
        }
        else
        {
            NodeOperation?.Render(canvas);
        }
    }
}
