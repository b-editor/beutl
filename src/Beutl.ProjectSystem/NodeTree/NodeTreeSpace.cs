using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

using Avalonia.Collections.Pooled;

using Beutl.Animation;
using Beutl.Collections;
using Beutl.Media;
using Beutl.NodeTree.Nodes.Group;
using Beutl.ProjectSystem;
using Beutl.Rendering;

namespace Beutl.NodeTree;

public class NodeGroup : NodeTreeSpace
{
    public static readonly CoreProperty<GroupInput?> InputProperty;
    public static readonly CoreProperty<GroupOutput?> OutputProperty;

    private GroupInput? _input;
    private GroupOutput? _output;

    static NodeGroup()
    {
        InputProperty = ConfigureProperty<GroupInput?, NodeGroup>(o => o.Input)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();

        OutputProperty = ConfigureProperty<GroupOutput?, NodeGroup>(o => o.Output)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();

        IdProperty.OverrideMetadata<NodeGroup>(new CorePropertyMetadata<Guid>("id"));
        NameProperty.OverrideMetadata<NodeGroup>(new CorePropertyMetadata<string>("name"));
    }

    public NodeGroup()
    {
        Id = Guid.NewGuid();
        Nodes.Attached += OnNodeAttached;
        Nodes.Detached += OnNodeDetached;
    }

    public GroupInput? Input
    {
        get => _input;
        set => SetAndRaise(InputProperty, ref _input, value);
    }

    public GroupOutput? Output
    {
        get => _output;
        set => SetAndRaise(OutputProperty, ref _output, value);
    }

    private void OnNodeTreeInvalidated(object? sender, EventArgs e)
    {
        RaiseInvalidated(new RenderInvalidatedEventArgs(this));
    }

    private void OnNodeInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        RaiseInvalidated(e);
    }

    private void OnNodeAttached(Node obj)
    {
        obj.NodeTreeInvalidated += OnNodeTreeInvalidated;
        obj.Invalidated += OnNodeInvalidated;
        if (obj is GroupInput groupInput)
        {
            Input = groupInput;
        }
        if (obj is GroupOutput groupOutput)
        {
            Output = groupOutput;
        }
    }

    private void OnNodeDetached(Node obj)
    {
        obj.NodeTreeInvalidated -= OnNodeTreeInvalidated;
        obj.Invalidated -= OnNodeInvalidated;
        if (obj == Input)
        {
            Input = null;
        }
        if (obj == Output)
        {
            Output = null;
        }
    }

    public void Evaluate(NodeEvaluationContext parentContext, object state)
    {
        if (state is List<NodeEvaluationContext[]> contexts)
        {
            foreach (NodeEvaluationContext[]? item in CollectionsMarshal.AsSpan(contexts))
            {
                foreach (NodeEvaluationContext? context in item)
                {
                    context._renderables = parentContext._renderables;

                    context.Node.PreEvaluate(context);
                    context.Node.Evaluate(context);
                    context.Node.PostEvaluate(context);
                }
            }
        }
    }

    public object InitializeForState(IRenderer renderer)
    {
        var evalContexts = new List<NodeEvaluationContext[]>();

        foreach (Node? lastNode in Nodes.Where(x => !x.Items.Any(x => x is IOutputSocket)))
        {
            var stack = new Stack<NodeEvaluationContext>();
            BuildNode(lastNode, stack);
            NodeEvaluationContext[] array = stack.ToArray();

            evalContexts.Add(array);
            foreach (NodeEvaluationContext item in array)
            {
                item.Clock = renderer.Clock;
                item.Renderer = renderer;
                item.List = array;
                item.Node.InitializeForContext(item);
            }
        }

        return evalContexts;
    }

    public void UninitializeForState(object? state)
    {
        if (state is List<NodeEvaluationContext[]> contexts)
        {
            foreach (NodeEvaluationContext[] item in CollectionsMarshal.AsSpan(contexts))
            {
                foreach (NodeEvaluationContext? context in item.AsSpan())
                {
                    context.Node.UninitializeForContext(context);
                }
            }

            contexts.Clear();
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
                Node? node2 = outputSocket.FindHierarchicalParent<Node>();
                if (node2 != null)
                {
                    BuildNode(node2, stack);
                }
            }
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args is CorePropertyChangedEventArgs e)
        {
            if (e.Property == InputProperty
                || e.Property == OutputProperty)
            {
                int index = -1;

                if (e.OldValue is Node oldNode)
                {
                    index = Nodes.IndexOf(oldNode);
                    Nodes.Remove(oldNode);
                }

                if (index == -1)
                {
                    index = Nodes.Count;
                }

                if (e.NewValue is Node newNode)
                {
                    if (e.Property == InputProperty && Nodes.Any(x => x is GroupInput))
                        return;
                    else if (e.Property == OutputProperty && Nodes.Any(x => x is GroupOutput))
                        return;

                    Nodes.Insert(index, newNode);
                }
            }
        }
    }
}

// Todo: NodeTreeModel
public abstract class NodeTreeSpace : Hierarchical, IAffectsRender
{
    private readonly HierarchicalList<Node> _nodes;

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public NodeTreeSpace()
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
