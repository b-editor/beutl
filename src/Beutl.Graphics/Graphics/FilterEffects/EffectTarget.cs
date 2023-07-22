using Beutl.Graphics.Rendering;
using Beutl.Media.Source;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class EffectTarget : IDisposable
{
    private object _target;

    public EffectTarget(FilterEffectNode node)
    {
        _target = node;
        Size = node.Bounds.Size;
    }

    public EffectTarget(Ref<SKSurface> surface, Size size)
    {
        _target = surface;
        Size = size;
    }

    public Size Size { get; }

    public FilterEffectNode? Node => _target as FilterEffectNode;

    public Ref<SKSurface>? Surface => _target as Ref<SKSurface>;

    public EffectTarget Clone()
    {
        if (Node != null)
        {
            return this;
        }
        else
        {
            return new EffectTarget(Surface!.Clone(), Size);
        }
    }

    public void Dispose()
    {
        Surface?.Dispose();
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

