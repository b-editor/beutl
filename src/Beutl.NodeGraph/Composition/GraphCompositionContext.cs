using Beutl.Composition;

using Beutl.Graphics.Rendering;

namespace Beutl.NodeGraph.Composition;

public sealed class GraphCompositionContext : CompositionContext
{
    public GraphCompositionContext(TimeSpan time) : base(time)
    {
    }

    internal GraphNode.Resource Resource { get; set; } = null!;

    internal GraphSnapshot Snapshot { get; set; } = null!;

    public CompositionTarget Target { get; internal set; }

    /// <summary>The ambient failure policy of the render that is evaluating this graph.</summary>
    internal RenderIntent RenderIntent { get; set; } = RenderIntent.Preview;

    public bool HasConnection(IInputPort port)
    {
        return Snapshot.HasInputConnection(Resource.SlotIndex, Resource.ItemIndexMap[port]);
    }

    public void CollectListInputValues<T>(ListInputPort<T> port, IList<T?> list)
    {
        Snapshot.CollectListInputValues(Resource.SlotIndex, Resource.ItemIndexMap[port], list);
    }

    public List<T?> CollectListInputValues<T>(ListInputPort<T> port)
    {
        var list = new List<T?>(port.Connections.Count);
        Snapshot.CollectListInputValues(Resource.SlotIndex, Resource.ItemIndexMap[port], list);
        return list;
    }
}
