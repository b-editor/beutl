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

    internal readonly PooledList<FEItemWrapper> _history = [];

    public EffectTarget(IGraphicNode node)
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

    [Obsolete()]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Size Size => Bounds.Size;

    public IGraphicNode? Node => _target as IGraphicNode;

    public Ref<SKSurface>? Surface => _target as Ref<SKSurface>;

    public bool IsEmpty => _target == null;

    public EffectTarget Clone()
    {
        if (Node != null)
        {
            return this;
        }
        else if (Surface != null)
        {
            var obj = new EffectTarget(Surface, OriginalBounds)
            {
                Bounds = Bounds
            };
            obj._history.AddRange(_history.Select(v => v.Inherit()));
            return obj;
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
        _history.Dispose();
    }

    public void Draw(ImmediateCanvas canvas)
    {
        if (Node != null)
        {
            canvas.DrawNode(Node);
        }
        else if (Surface != null)
        {
            canvas.DrawSurface(Surface.Value, default);
        }
    }
}
