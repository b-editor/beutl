using Beutl.Collections;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.NodeTree;

public abstract class NodeTreeModel : Hierarchical, INotifyEdited
{
    public static readonly CoreProperty<HierarchicalList<Node>> NodesProperty;
    private readonly HierarchicalList<Node> _nodes;

    public event EventHandler? Edited;

    static NodeTreeModel()
    {
        NodesProperty = ConfigureProperty<HierarchicalList<Node>, NodeTreeModel>(nameof(Nodes))
            .Accessor(o => o.Nodes, (o, v) => o.Nodes = v)
            .Register();
    }

    public NodeTreeModel()
    {
        _nodes = new HierarchicalList<Node>(this);
    }

    public HierarchicalList<Node> Nodes
    {
        get => _nodes;
        set => _nodes.Replace(value);
    }

    protected void RaiseInvalidated(EventArgs args)
    {
        Edited?.Invoke(this, args);
    }

    public ISocket? FindSocket(Guid id)
    {
        foreach (Node node in Nodes.GetMarshal().Value)
        {
            foreach (INodeItem item in node.Items.GetMarshal().Value)
            {
                if (item is ISocket socket
                    && socket.Id == id)
                {
                    return socket;
                }
            }
        }

        return null;
    }
}
