using System.Text.Json.Nodes;

using Beutl.Collections;
using Beutl.Media;

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

    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);

        if (json.TryGetPropertyValue(nameof(Nodes), out JsonNode? nodesNode)
            && nodesNode is JsonArray nodesArray)
        {
            foreach (JsonObject nodeJson in nodesArray.OfType<JsonObject>())
            {
                if (nodeJson.TryGetDiscriminator(out Type? type)
                    && Activator.CreateInstance(type) is Node node)
                {
                    // Todo: 型が見つからない場合、SourceOperatorと同じようにする
                    node.ReadFromJson(nodeJson);
                    Nodes.Add(node);
                }
            }
        }
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);

        Span<Node> nodes = _nodes.GetMarshal().Value;
        if (nodes.Length > 0)
        {
            var array = new JsonArray();

            foreach (Node item in nodes)
            {
                var itemJson = new JsonObject();
                item.WriteToJson(itemJson);
                itemJson.WriteDiscriminator(item.GetType());

                array.Add(itemJson);
            }

            json[nameof(Nodes)] = array;
        }
    }
}
