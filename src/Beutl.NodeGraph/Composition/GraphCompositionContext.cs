using Beutl.Composition;

namespace Beutl.NodeGraph.Composition;

public sealed class GraphCompositionContext : CompositionContext
{
    public GraphCompositionContext(MediaTime time) : base(time)
    {
    }

    internal GraphNode.Resource Resource { get; set; } = null!;

    internal GraphSnapshot Snapshot { get; set; } = null!;

    public bool HasConnection(IInputPort port)
    {
        return Snapshot.HasInputConnection(Resource.SlotIndex, Resource.ItemIndexMap[port]);
    }

    public List<T?> CollectListInputValues<T>(ListInputPort<T> port)
    {
        var list = new List<T?>(port.Connections.Count);
        Snapshot.CollectListInputValues(Resource.SlotIndex, Resource.ItemIndexMap[port], list);
        return list;
    }
}
