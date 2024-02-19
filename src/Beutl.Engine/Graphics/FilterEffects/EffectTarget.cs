using Beutl.Graphics.Rendering;
using Beutl.Media.Source;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class EffectTarget : IDisposable
{
    public static readonly EffectTarget Empty = new();

    private object? _target;

    public EffectTarget(FilterEffectNode node)
    {
        _target = node;
        OriginalBounds = node.OriginalBounds;
        Bounds = node.OriginalBounds;
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

    public FilterEffectNode? Node => _target as FilterEffectNode;

    public Ref<SKSurface>? Surface => _target as Ref<SKSurface>;

    public EffectTarget Clone()
    {
        if (Node != null)
        {
            return this;
        }
        else if (Surface != null)
        {
            return new EffectTarget(Surface, OriginalBounds)
            {
                Bounds = Bounds
            };
        }
        else
        {
            return this;
        }
    }

    public void Dispose()
    {
        Surface?.Dispose();
        _target = null;
        OriginalBounds = default;
    }

    public void Draw(ImmediateCanvas canvas)
    {
        if (Node != null)
        {
            foreach (IGraphicNode item in Node.Children)
            {
                canvas.DrawNode(item);
            }
        }
        else if (Surface != null)
        {
            canvas.DrawSurface(Surface.Value, default);
        }
    }
}

