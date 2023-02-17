using System.Runtime.InteropServices;

using Avalonia.Collections.Pooled;

using Beutl.Animation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Rendering;

namespace Beutl.NodeTree;

public class LayerNodeTreeModel : NodeTreeSpace
{
    // 評価する順番
    private readonly List<NodeEvaluationContext[]> _evalContexts = new();
    private bool _isDirty = true;

    public LayerNodeTreeModel()
    {
        Nodes.Attached += OnNodeAttached;
        Nodes.Detached += OnNodeDetached;
    }

    private void OnNodeTreeInvalidated(object? sender, EventArgs e)
    {
        _isDirty = true;
        RaiseInvalidated(new RenderInvalidatedEventArgs(this));
    }

    private void OnNodeInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        RaiseInvalidated(e);
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

            foreach (Node? lastNode in Nodes.Where(x => !x.Items.Any(x => x is IOutputSocket)))
            {
                var stack = new Stack<NodeEvaluationContext>();
                BuildNode(lastNode, stack);
                NodeEvaluationContext[] array = stack.ToArray();

                _evalContexts.Add(array);
                foreach (NodeEvaluationContext item in array)
                {
                    item.Clock = clock;
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
}
