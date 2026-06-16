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
        // A RenderTarget is a concrete buffer; map Unbounded to At(1) for coherent density.
        Scale = scale.IsUnbounded ? EffectiveScale.At(1f) : scale;
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
            // Dest size from buffer footprint (pixels / density), not from Bounds — Bounds may be
            // inflated by downstream effects. Density-1 uses a bare point blit; otherwise Mitchell.
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
