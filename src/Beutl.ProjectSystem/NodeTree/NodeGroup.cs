using System.ComponentModel;
using System.Runtime.InteropServices;

using Beutl.Animation;
using Beutl.Media;
using Beutl.NodeTree.Nodes.Group;
using Beutl.Rendering;

namespace Beutl.NodeTree;

public class NodeGroup : NodeTreeModel
{
    public static readonly CoreProperty<GroupInput?> InputProperty;
    public static readonly CoreProperty<GroupOutput?> OutputProperty;

    private GroupInput? _input;
    private GroupOutput? _output;

    static NodeGroup()
    {
        InputProperty = ConfigureProperty<GroupInput?, NodeGroup>(o => o.Input)
            .Register();

        OutputProperty = ConfigureProperty<GroupOutput?, NodeGroup>(o => o.Output)
            .Register();
    }

    public NodeGroup()
    {
        Nodes.Attached += OnNodeAttached;
        Nodes.Detached += OnNodeDetached;
    }

    [NotAutoSerialized]
    public GroupInput? Input
    {
        get => _input;
        set => SetAndRaise(InputProperty, ref _input, value);
    }

    [NotAutoSerialized]
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

#pragma warning disable CA1822 // メンバーを static に設定します
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

    public object InitializeForState(IRenderer renderer, IClock clock)
    {
        var evalContexts = new List<NodeEvaluationContext[]>();

        foreach (Node? lastNode in Nodes.Where(x => !x.Items.Any(x => x is IOutputSocket)))
        {
            var stack = new Stack<NodeEvaluationContext>();
            BuildNode(lastNode, stack);
            NodeEvaluationContext[] array = [.. stack];

            evalContexts.Add(array);
            foreach (NodeEvaluationContext item in array)
            {
                item.Clock = clock;
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
#pragma warning restore CA1822 // メンバーを static に設定します

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
