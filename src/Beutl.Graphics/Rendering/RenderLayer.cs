using System.Runtime.InteropServices;

using Beutl.Audio;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Rendering;

public class RenderLayer : IRenderLayer
{
    private TimeSpan? _lastTimeSpan;
    private RenderLayerSpan? _lastTimeResult;
    private readonly List<RenderLayerSpan> _nodes = new();

    public RenderLayerSpan? this[TimeSpan timeSpan] => Get(timeSpan);

    public IRenderer? Renderer { get; private set; }

    private void OnNodeInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        if (Renderer is { } renderer
            && sender is RenderLayerSpan span
            && span.Value is { } renderable)
        {
            renderer.AddDirty(renderable);
        }
    }

    public void AddNode(RenderLayerSpan node)
    {
        _nodes.Add(node);
        _lastTimeSpan = null;
        _lastTimeResult = null;

        node.Invalidated += OnNodeInvalidated;
        node.AttachToRenderLayer(this);
    }

    public void RemoveNode(RenderLayerSpan node)
    {
        _nodes.Remove(node);
        _lastTimeSpan = null;
        _lastTimeResult = null;

        node.Invalidated -= OnNodeInvalidated;
        node.DetachFromRenderLayer();
    }

    public bool ContainsNode(RenderLayerSpan node)
    {
        return _nodes.Contains(node);
    }

    private RenderLayerSpan? Get(TimeSpan timeSpan)
    {
        if (_lastTimeSpan.HasValue && _lastTimeSpan == timeSpan)
        {
            return _lastTimeResult;
        }

        _lastTimeSpan = timeSpan;

        foreach (RenderLayerSpan node in CollectionsMarshal.AsSpan(_nodes))
        {
            if (node.Range.Contains(timeSpan))
            {
                _lastTimeResult = node;
                return node;
            }
        }

        _lastTimeResult = null;
        return null;
    }

    public Span<RenderLayerSpan> GetRange(TimeSpan start, TimeSpan duration)
    {
        var list = new List<RenderLayerSpan>();
        var range = new TimeRange(start, duration);

        foreach (RenderLayerSpan node in CollectionsMarshal.AsSpan(_nodes))
        {
            if (node.Range.Intersects(range))
            {
                list.Add(node);
            }
        }

        return CollectionsMarshal.AsSpan(list);
    }

    public void RenderGraphics()
    {
        if (Renderer is { Clock.CurrentTime: { } timeSpan } renderer)
        {
            RenderLayerSpan? layer = Get(timeSpan);
            if (layer != null && layer.Value is Drawable drawable)
            {
                drawable.Render(renderer);
            }
        }
    }

    public void RenderAudio()
    {
        if (Renderer is { Clock.AudioStartTime: { } timeSpan } renderer)
        {
            Span<RenderLayerSpan> span = GetRange(timeSpan, TimeSpan.FromSeconds(1));
            foreach (RenderLayerSpan item in span)
            {
                if (item.Value is Sound sound)
                {
                    sound.Render(renderer);
                }
            }
        }
    }

    public void AttachToRenderer(IRenderer renderer)
    {
        if (Renderer != null && Renderer != renderer)
        {
            throw new InvalidOperationException();
        }

        Renderer = renderer;
    }

    public void DetachFromRenderer()
    {
        Renderer = null;
    }
}
