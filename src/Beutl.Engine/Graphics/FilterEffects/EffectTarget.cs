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

    public EffectTarget(Ref<SKSurface> surface, Rect originalBounds)
    {
        _target = surface.Clone();
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

    public Ref<SKSurface>? Surface => _target as Ref<SKSurface>;

    public bool IsEmpty => _target == null;

    public EffectTarget Clone()
    {
        if (Surface != null)
        {
            return new EffectTarget(Surface, OriginalBounds) { Bounds = Bounds };
        }
        else
        {
            return this;
        }
    }

    public void Dispose()
    {
        Surface?.Dispose();
        NodeOperation?.Dispose();
        _target = null;
        OriginalBounds = default;
    }

    public void Draw(ImmediateCanvas canvas)
    {
        if (Surface != null)
        {
            canvas.DrawSurface(Surface.Value, default);
        }
        else if (NodeOperation != null)
        {
            NodeOperation.Render(canvas);
        }
    }
}
