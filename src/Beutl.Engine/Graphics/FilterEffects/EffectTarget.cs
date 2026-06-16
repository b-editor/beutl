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
        // feature 003 (I3): a RenderTarget is a CONCRETE bitmap buffer, never an Unbounded vector. An
        // Unbounded scale here is contradictory — Draw reads it as density 1 while CustomFilterEffectContext.Open
        // reads the buffer at WorkingScale. Map Unbounded => At(1) so the density is always concrete and coherent.
        Scale = scale.IsUnbounded ? EffectiveScale.At(1f) : scale;
    }

    public EffectTarget()
    {
    }

    public Rect OriginalBounds { get; set; }

    public Rect Bounds { get; set; }

    /// <summary>
    /// The supply density of this target's backing pixels (feature 003), driving mixed-scale reconciliation.
    /// <see cref="EffectiveScale.Unbounded"/> for a vector <see cref="NodeOperation"/>; a concrete
    /// <see cref="EffectiveScale.At"/> for a flushed <see cref="RenderTarget"/> buffer at its working scale.
    /// </summary>
    public EffectiveScale Scale { get; init; }

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
            // LOGICAL footprint = pixel size ÷ w (origin-anchored at (0,0)) so the ambient CTM maps it. Derive
            // the footprint from the buffer, NOT OriginalBounds: a downstream filter (e.g. blur/shadow wrapped in
            // DelayAnimation) can inflate OriginalBounds while the buffer still covers the original area, so
            // OriginalBounds would stretch it. Keep the bare point blit ONLY on a density-1 canvas
            // (byte-identical pre-feature path); on a scaled canvas it would be CTM-resampled with NEAREST, so
            // route through the Mitchell blit instead — same geometry, consistent kernel. Key off the CURRENT
            // density (1 ⇔ active CTM is device-1:1, incl. inside a PushDeviceSpace block), not the immutable
            // surface density.
            if ((Scale.IsUnbounded || Scale.Value == 1f) && canvas.Density == 1f)
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
