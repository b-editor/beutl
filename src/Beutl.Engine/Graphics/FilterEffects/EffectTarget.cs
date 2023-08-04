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
        Size = node.Bounds.Size;
    }

    public EffectTarget(Ref<SKSurface> surface, Size size)
    {
        _target = surface.Clone();
        Size = size;
    }

    public EffectTarget()
    {
    }

    public Size Size { get; private set; }

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
            return new EffectTarget(Surface, Size);
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
        Size = default;
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
            canvas.Canvas.DrawSurface(Surface.Value, default);
        }
    }
}

