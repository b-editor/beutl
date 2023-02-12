using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

using Avalonia.Collections.Pooled;

using Beutl.Animation;
using Beutl.Collections;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Rendering;

namespace Beutl.NodeTree;

public class NodeTreeSpace : Element, IAffectsRender
{
    private readonly LogicalList<Node> _nodes;
    // 評価する順番
    private readonly List<NodeEvaluationContext[]> _evalContexts = new();
    private bool _isDirty = true;

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public NodeTreeSpace()
    {
        _nodes = new LogicalList<Node>(this);
        _nodes.Attached += OnNodeAttached;
        _nodes.Detached += OnNodeDetached;
    }

    private void OnNodeTreeInvalidated(object? sender, EventArgs e)
    {
        _isDirty = true;
        Invalidated?.Invoke(this, new RenderInvalidatedEventArgs(this));
    }

    private void OnNodeInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Invalidated?.Invoke(this, e);
    }

    private void OnNodeAttached(Node obj)
    {
        _isDirty = true;
        obj.NodeTreeInvalidated += OnNodeTreeInvalidated;
        obj.Invalidated += OnNodeInvalidated;
    }

    private void OnNodeDetached(Node obj)
    {
        _isDirty = true;
        obj.NodeTreeInvalidated -= OnNodeTreeInvalidated;
        obj.Invalidated -= OnNodeInvalidated;
    }

    public ICoreList<Node> Nodes => _nodes;

    public void Evaluate(IClock clock, Layer layer)
    {
        Build(clock);
        using var list = new PooledList<Renderable>();

        foreach (NodeEvaluationContext[]? item in CollectionsMarshal.AsSpan(_evalContexts))
        {
            foreach (NodeEvaluationContext? context in item)
            {
                context._renderables = list;

                context.Node.PreEvaluate(context);
                context.Node.Evaluate(context);
                context.Node.PostEvaluate(context);
            }
        }

        layer.Span.Value.Replace(list);
    }

    public ISocket? FindSocket(Guid id)
    {
        foreach (Node node in _nodes.GetMarshal().Value)
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

    private void Uninitialize()
    {
        foreach (var item in CollectionsMarshal.AsSpan(_evalContexts))
        {
            foreach (NodeEvaluationContext? context in item.AsSpan())
            {
                context.Node.UninitializeForContext(context);
            }
        }

        _evalContexts.Clear();
    }

    private void Build(IClock clock)
    {
        if (_isDirty)
        {
            Uninitialize();
            int nextId = 0;

            foreach (Node? lastNode in _nodes.Where(x => !x.Items.Any(x => x is IOutputSocket)))
            {
                var stack = new Stack<NodeEvaluationContext>();
                BuildNode(lastNode, stack);
                NodeEvaluationContext[] array = stack.ToArray();

                _evalContexts.Add(array);
                foreach (NodeEvaluationContext item in array)
                {
                    item.Clock = clock;
                    item.Id = nextId;
                    item.List = array;
                    item.Node.InitializeForContext(item);
                }

                nextId++;
            }

            _isDirty = false;
        }
    }

    private void BuildNode(Node node, Stack<NodeEvaluationContext> stack)
    {
        if (!stack.Any(x => x.Node == node))
        {
            var context = new NodeEvaluationContext(node);
            stack.Push(context);
        }

        for (int i = 0; i < node.Items.Count; i++)
        {
            INodeItem? item = node.Items[i];
            if (item is IInputSocket { Connection.Output: { } outputSocket })
            {
                Node? node2 = outputSocket.FindLogicalParent<Node>();
                if (node2 != null)
                {
                    BuildNode(node2, stack);
                }
            }
        }
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);

        if (json is JsonObject jobject)
        {
            if (jobject.TryGetPropertyValue("nodes", out JsonNode? nodesNode)
                && nodesNode is JsonArray nodesArray)
            {
                foreach (JsonObject nodeJson in nodesArray.OfType<JsonObject>())
                {
                    if (nodeJson.TryGetPropertyValue("@type", out JsonNode? atTypeNode)
                        && atTypeNode is JsonValue atTypeValue
                        && atTypeValue.TryGetValue(out string? atType))
                    {
                        var type = TypeFormat.ToType(atType);
                        Node? node = null;

                        if (type?.IsAssignableTo(typeof(Node)) ?? false)
                        {
                            node = Activator.CreateInstance(type) as Node;
                        }

                        // Todo: 型が見つからない場合、SourceOperatorと同じようにする
                        if (node != null)
                        {
                            node.ReadFromJson(nodeJson);
                            Nodes.Add(node);
                        }
                    }
                }
            }
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);

        if (json is JsonObject jobject)
        {
            Span<Node> nodes = _nodes.GetMarshal().Value;
            if (nodes.Length > 0)
            {
                var array = new JsonArray();

                foreach (Node item in nodes)
                {
                    JsonNode jsonNode = new JsonObject();
                    item.WriteToJson(ref jsonNode);
                    jsonNode["@type"] = TypeFormat.ToString(item.GetType());

                    array.Add(jsonNode);
                }

                jobject["nodes"] = array;
            }
        }
    }
}
