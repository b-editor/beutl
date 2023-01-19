using System.Runtime.InteropServices;

using Avalonia.Collections.Pooled;

using Beutl.Animation;
using Beutl.Collections;
using Beutl.ProjectSystem;
using Beutl.Rendering;

namespace Beutl.NodeTree;

public class NodeTreeSpace : Element
{
    private readonly LogicalList<INode> _nodes;
    // 評価する順番
    private readonly List<INode[]> _evaluationList = new();
    private bool _isDirty = true;

    public NodeTreeSpace()
    {
        _nodes = new LogicalList<INode>(this);
        _nodes.Attached += OnNodeAttached;
        _nodes.Detached += OnNodeDetached;
    }

    private void OnNodeTreeInvalidated(object? sender, EventArgs e)
    {
        _isDirty = true;
    }

    private void OnNodeAttached(INode obj)
    {
        obj.NodeTreeInvalidated += OnNodeTreeInvalidated;
        Build();
    }

    private void OnNodeDetached(INode obj)
    {
        obj.NodeTreeInvalidated -= OnNodeTreeInvalidated;
        Build();
    }

    public ICoreList<INode> Nodes => _nodes;

    public void Evaluate(IClock clock, Layer layer)
    {
        Build();
        using var list = new PooledList<Renderable>();

        foreach (INode[]? item in CollectionsMarshal.AsSpan(_evaluationList))
        {
            var context = new EvaluationContext(clock, item, list);
            foreach (INode? node in item)
            {
                node.Evaluate(context);
            }
        }

        layer.Span.Value.Replace(list);
    }

    private void Build()
    {
        if (_isDirty)
        {
            _evaluationList.Clear();

            foreach (INode? lastNode in _nodes.Where(x => !x.Items.Any(x => x is IOutputSocket)))
            {
                var stack = new Stack<INode>();
                BuildNode(lastNode, stack);
                _evaluationList.Add(stack.ToArray());
            }

            _isDirty = false;
        }
    }

    private void BuildNode(INode node, Stack<INode> stack)
    {
        if (!stack.Contains(node))
        {
            stack.Push(node);
        }

        for (int i = 0; i < node.Items.Count; i++)
        {
            INodeItem? item = node.Items[i];
            if (item is IInputSocket { Connection.Output: { } outputSocket })
            {
                INode? node2 = outputSocket.FindLogicalParent<INode>();
                if (node2 != null)
                {
                    BuildNode(node2, stack);
                }
            }
        }
    }
}
