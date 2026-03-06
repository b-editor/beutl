using Beutl.Composition;

namespace Beutl.NodeTree.Rendering;

public sealed class NodeCompositionContext : CompositionContext
{
    public NodeCompositionContext(TimeSpan time) : base(time)
    {
    }

    internal Node.Resource Resource { get; set; } = null!;

    internal NodeTreeSnapshot Snapshot { get; set; } = null!;

    public CompositionTarget Target { get; internal set; }

    public bool HasConnection(IInputSocket socket)
    {
        return Snapshot.HasInputConnection(Resource.SlotIndex, Resource.ItemIndexMap[socket]);
    }

    public void CollectListInputValues<T>(ListInputSocket<T> socket, IList<T?> list)
    {
        Snapshot.CollectListInputValues(Resource.SlotIndex, Resource.ItemIndexMap[socket], list);
    }

    public List<T?> CollectListInputValues<T>(ListInputSocket<T> socket)
    {
        var list = new List<T?>(socket.Connections.Count);
        Snapshot.CollectListInputValues(Resource.SlotIndex, Resource.ItemIndexMap[socket], list);
        return list;
    }
}
