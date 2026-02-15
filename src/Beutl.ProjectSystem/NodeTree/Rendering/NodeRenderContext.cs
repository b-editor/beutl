using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.NodeTree.Rendering;

public sealed class NodeRenderContext : RenderContext
{
    internal IList<EngineObject>? _renderables;

    public NodeRenderContext(TimeSpan time) : base(time)
    {
    }

    internal Node.Resource Resource { get; set; } = null!;

    internal NodeTreeSnapshot Snapshot { get; set; } = null!;

    public EvaluationTarget Target { get; internal set; }

    public void AddRenderable(EngineObject renderable)
    {
        _renderables?.Add(renderable);
    }

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
