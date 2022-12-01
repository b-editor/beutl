using System.Buffers;
using System.Runtime.InteropServices;

using Beutl.Audio;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Rendering;

public class LayerContext : ILayerContext
{
    private TimeSpan? _lastTimeSpan;
    private LayerNode? _lastTimeResult;
    private readonly List<LayerNode> _nodes = new();

    public LayerNode? this[TimeSpan timeSpan] => Get(timeSpan);

    public void AddNode(LayerNode node)
    {
        _nodes.Add(node);
        _lastTimeSpan = null;
        _lastTimeResult = null;
    }

    public void RemoveNode(LayerNode node)
    {
        _nodes.Remove(node);
        _lastTimeSpan = null;
        _lastTimeResult = null;
    }

    public bool ContainsNode(LayerNode node)
    {
        return _nodes.Contains(node);
    }

    private LayerNode? Get(TimeSpan timeSpan)
    {
        if (_lastTimeSpan.HasValue && _lastTimeSpan == timeSpan)
        {
            return _lastTimeResult;
        }

        _lastTimeSpan = timeSpan;

        foreach (LayerNode node in CollectionsMarshal.AsSpan(_nodes))
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

    public Span<LayerNode> GetRange(TimeSpan start, TimeSpan duration)
    {
        var list = new List<LayerNode>();
        var range = new TimeRange(start, duration);

        foreach (LayerNode node in CollectionsMarshal.AsSpan(_nodes))
        {
            if (node.Range.Intersects(range))
            {
                list.Add(node);
            }
        }

        return CollectionsMarshal.AsSpan(list);
    }

    public void RenderGraphics(IRenderer renderer, TimeSpan timeSpan)
    {
        LayerNode? layer = Get(timeSpan);
        if (layer != null && layer.Value is Drawable drawable)
        {
            drawable.Render(renderer);
        }
    }

    public void RenderAudio(IRenderer renderer, TimeSpan timeSpan)
    {
        Span<LayerNode> span = GetRange(timeSpan, TimeSpan.FromSeconds(1));
        foreach (LayerNode item in span)
        {
            if(item.Value is Sound sound)
            {
                sound.Render(renderer);
            }
        }
    }
}
