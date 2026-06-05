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
            canvas.DrawRenderTarget(RenderTarget, default);
        }
        else
        {
            NodeOperation?.Render(canvas);
        }
    }
}
