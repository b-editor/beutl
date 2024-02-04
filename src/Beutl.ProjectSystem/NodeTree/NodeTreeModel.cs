using System.Text.Json.Nodes;

using Beutl.Collections;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.NodeTree;

public abstract class NodeTreeModel : Hierarchical, IAffectsRender
{
    private readonly HierarchicalList<Node> _nodes;

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public NodeTreeModel()
    {
        _nodes = new HierarchicalList<Node>(this);
    }

    public ICoreList<Node> Nodes => _nodes;

    protected void RaiseInvalidated(RenderInvalidatedEventArgs args)
    {
        Invalidated?.Invoke(this, args);
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

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Nodes), Nodes);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<Node[]>(nameof(Nodes)) is { } nodes)
        {
            Nodes.Replace(nodes);
        }
    }
}
