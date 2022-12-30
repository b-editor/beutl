using System.ComponentModel;
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

    public void AddNode(RenderLayerSpan node)
    {
        _nodes.Add(node);
        _lastTimeSpan = null;
        _lastTimeResult = null;
    }

    public void RemoveNode(RenderLayerSpan node)
    {
        _nodes.Remove(node);
        _lastTimeSpan = null;
        _lastTimeResult = null;
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

    public void RenderGraphics(IRenderer renderer, TimeSpan timeSpan)
    {
        RenderLayerSpan? layer = Get(timeSpan);
        if (layer != null && layer.Value is Drawable drawable)
        {
            drawable.Render(renderer);
        }
    }

    public void RenderAudio(IRenderer renderer, TimeSpan timeSpan)
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
