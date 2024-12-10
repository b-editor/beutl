using System.ComponentModel;
using Beutl.Collections.Pooled;
using Beutl.Graphics.Rendering;
using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class EffectTarget : IDisposable
{
    [Obsolete("Use a constructor with no parameters.")]
    public static readonly EffectTarget Empty = new();

    private object? _target;

    public EffectTarget(RenderNodeOperation node)
    {
        _target = node;
        OriginalBounds = node.Bounds;
        Bounds = node.Bounds;
    }

    public EffectTarget(RenderTarget renderTarget, Rect originalBounds)
    {
        _target = renderTarget.ShallowCopy();
        OriginalBounds = originalBounds;
        Bounds = originalBounds;
    }

    public EffectTarget()
    {
    }

    public Rect OriginalBounds { get; set; }

    public Rect Bounds { get; set; }

    [Obsolete]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Size Size => Bounds.Size;

    public RenderNodeOperation? NodeOperation => _target as RenderNodeOperation;
    
    public RenderTarget? RenderTarget => _target as RenderTarget;

    public bool IsEmpty => _target == null;

    public EffectTarget Clone()
    {
        if (RenderTarget != null)
        {
            return new EffectTarget(RenderTarget, OriginalBounds) { Bounds = Bounds };
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
