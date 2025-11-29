using System.Runtime.InteropServices;

using Beutl.Animation;
using Beutl.Collections.Pooled;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.NodeTree;

public class ElementNodeTreeModel : NodeTreeModel
{
    // 評価する順番
    private readonly List<NodeEvaluationContext[]> _evalContexts = [];
    private bool _isDirty = true;

    public ElementNodeTreeModel()
    {
        Nodes.Attached += OnNodeAttached;
        Nodes.Detached += OnNodeDetached;
    }

    private void OnNodeTreeInvalidated(object? sender, EventArgs e)
    {
        _isDirty = true;
        RaiseInvalidated(new RenderInvalidatedEventArgs(this));
    }

    private void OnNodeEdited(object? sender, EventArgs e)
    {
        RaiseInvalidated(e);
    }

    private void OnNodeAttached(Node obj)
    {
        _isDirty = true;
        obj.NodeTreeInvalidated += OnNodeTreeInvalidated;
        obj.Edited += OnNodeEdited;
    }

    private void OnNodeDetached(Node obj)
    {
        _isDirty = true;
        obj.NodeTreeInvalidated -= OnNodeTreeInvalidated;
        obj.Edited -= OnNodeEdited;
    }

    public PooledList<EngineObject> Evaluate(EvaluationTarget target, IRenderer renderer, Element element)
    {
        _ = target;
        Build(renderer);

        var list = new PooledList<EngineObject>();
        try
        {
            foreach (NodeEvaluationContext[]? item in CollectionsMarshal.AsSpan(_evalContexts))
            {
                foreach (NodeEvaluationContext? context in item)
                {
                    context.Target = target;
                    context._renderables = list;

                    context.Node.PreEvaluate(context);
                    context.Node.Evaluate(context);
                    context.Node.PostEvaluate(context);
                }
            }

            // Todo: LayerOutputNodeに移動
            foreach (EngineObject item in list.Span)
            {
                item.ZIndex = element.ZIndex;
                item.TimeRange = new TimeRange(element.Start, element.Length);
            }

            return list;
        }
        catch
        {
            list.Dispose();
            throw;
        }
    }

    private void Uninitialize()
    {
        foreach (NodeEvaluationContext[]? item in CollectionsMarshal.AsSpan(_evalContexts))
        {
            foreach (NodeEvaluationContext? context in item.AsSpan())
            {
                context.Node.UninitializeForContext(context);
            }
        }

        _evalContexts.Clear();
    }

    private void Build(IRenderer renderer)
    {
        if (_isDirty)
        {
            Uninitialize();
            int nextId = 0;
            using var stack = new PooledList<NodeEvaluationContext>();

            foreach (Node? lastNode in Nodes.Where(x => !x.Items.Any(x => x is IOutputSocket)))
            {
                BuildNode(lastNode, stack);
                NodeEvaluationContext[] array = [.. stack];
                Array.Reverse(array);

                _evalContexts.Add(array);
                foreach (NodeEvaluationContext item in array)
                {
                    item.Renderer = renderer;
                    item.List = array;
                    item.Node.InitializeForContext(item);
                }

                stack.Clear();
                nextId++;
            }

            _isDirty = false;
        }
    }

    private void BuildNode(Node node, PooledList<NodeEvaluationContext> stack)
    {
        if (stack.FirstOrDefault(x => x.Node == node) is { } context)
        {
            //
            stack.Remove(context);
            stack.Add(context);
        }
        else
        {
            context = new NodeEvaluationContext(node);
            stack.Add(context);
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
}
