using System.Runtime.InteropServices;

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
            if (node.Start <= timeSpan && timeSpan < node.Start + node.Duration)
            {
                _lastTimeResult = node;
                return node;
            }
        }

        _lastTimeResult = null;
        return null;
    }
}
