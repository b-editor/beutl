using System.Diagnostics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Rendering;

namespace Beutl.NodeTree;

public class NodeTreeEvaluator : IDisposable
{
    private readonly NodeTreeModel _model;
    private readonly List<NodeEvaluationContext[]> _evalContexts = [];
    private bool _isDirty = true;

    public NodeTreeEvaluator(NodeTreeModel model)
    {
        _model = model;
        _model.TopologyChanged += OnTopologyChanged;
    }

    public IReadOnlyList<NodeEvaluationContext[]> EvalContexts => _evalContexts;

    private void OnTopologyChanged(object? sender, EventArgs e)
    {
        _isDirty = true;
    }

    public void Build(IRenderer renderer)
    {
        if (!_isDirty) return;

        Uninitialize();

        int nodeCount = _model.Nodes.Count;
        if (nodeCount == 0)
        {
            _isDirty = false;
            return;
        }

        // Phase 1: 隣接リストと入次数を構築
        var inDegree = new Dictionary<Node, int>(nodeCount);
        var adjacency = new Dictionary<Node, List<Node>>(nodeCount);

        foreach (Node node in _model.Nodes)
        {
            inDegree[node] = 0;
            adjacency[node] = [];
        }

        foreach (Node node in _model.Nodes)
        {
            for (int i = 0; i < node.Items.Count; i++)
            {
                INodeItem item = node.Items[i];
                if (item is IListInputSocket listInputSocket)
                {
                    foreach (var connection in listInputSocket.Connections)
                    {
                        Node? upstream = connection.Value?.Output.Value?
                            .FindHierarchicalParent<Node>();
                        if (upstream != null && inDegree.ContainsKey(upstream))
                        {
                            adjacency[upstream].Add(node);
                            inDegree[node]++;
                        }
                    }
                }
                else if (item is IInputSocket inputSocket
                    && inputSocket.Connection.Value?.Output.Value is { } outputSocket)
                {
                    Node? upstream = outputSocket.FindHierarchicalParent<Node>();
                    if (upstream != null && inDegree.ContainsKey(upstream))
                    {
                        adjacency[upstream].Add(node);
                        inDegree[node]++;
                    }
                }
            }
        }

        // Phase 2: Kahn's アルゴリズム (BFS トポロジカルソート)
        var queue = new Queue<Node>();
        var sorted = new List<Node>(nodeCount);

        foreach (var (node, degree) in inDegree)
        {
            if (degree == 0)
                queue.Enqueue(node);
        }

        while (queue.Count > 0)
        {
            Node current = queue.Dequeue();
            sorted.Add(current);

            foreach (Node downstream in adjacency[current])
            {
                if (--inDegree[downstream] == 0)
                    queue.Enqueue(downstream);
            }
        }

        // Phase 3: サイクル検出
        if (sorted.Count != nodeCount)
        {
            Debug.WriteLine(
                $"NodeTreeEvaluator: Cycle detected. " +
                $"{nodeCount - sorted.Count} node(s) in cycle(s) were skipped.");
        }

        // Phase 4: 単一の評価コンテキスト配列を生成
        var contexts = new NodeEvaluationContext[sorted.Count];
        for (int i = 0; i < sorted.Count; i++)
        {
            contexts[i] = new NodeEvaluationContext(sorted[i]);
        }

        _evalContexts.Add(contexts);

        foreach (NodeEvaluationContext ctx in contexts)
        {
            ctx.Renderer = renderer;
            ctx.List = contexts;
            ctx.Node.InitializeForContext(ctx);
        }

        _isDirty = false;
    }

    public void Evaluate()
    {
        foreach (NodeEvaluationContext[]? item in CollectionsMarshal.AsSpan(_evalContexts))
        {
            foreach (NodeEvaluationContext? context in item)
            {
                context.Node.PreEvaluate(context);
                context.Node.Evaluate(context);
                context.Node.PostEvaluate(context);
            }
        }
    }

    public void Uninitialize()
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

    public void Dispose()
    {
        Uninitialize();
        _model.TopologyChanged -= OnTopologyChanged;
    }
}
