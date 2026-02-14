using System.Diagnostics;
using System.Runtime.InteropServices;
using Beutl.Collections;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics.Rendering;

namespace Beutl.NodeTree.Rendering;

public sealed class NodeTreeSnapshot : IDisposable
{
    private Node.Resource[] _resources = [];
    private NodeRenderContext[] _contexts = [];
    private ConnectionSnapshot[] _connections = [];
    private readonly Dictionary<(int, int), List<int>> _outputConnectionMap = new();
    private readonly Dictionary<(int, int), List<int>> _inputConnectionMap = new();
    private readonly HashSet<(int, int)> _connectedInputs = [];
    private bool _isDirty = true;
    private IRenderer? _renderer;

    public void MarkDirty() => _isDirty = true;

    public void Build(NodeTreeModel model, IRenderer renderer)
    {
        if (!_isDirty) return;

        _renderer = renderer;

        // 既存リソースをクリーンアップ
        Uninitialize();

        int nodeCount = model.Nodes.Count;
        if (nodeCount == 0)
        {
            _resources = [];
            _contexts = [];
            _connections = [];
            _outputConnectionMap.Clear();
            _connectedInputs.Clear();
            _isDirty = false;
            return;
        }

        // 隣接リストと入次数を構築
        var (inDegree, adjacency) = BuildAdjacencyAndInDegree(model.Nodes);

        // Kahn's アルゴリズム (BFS トポロジカルソート)
        var sorted = TopologicalSort(inDegree, adjacency);

        // サイクル検出
        if (sorted.Count != nodeCount)
        {
            Debug.WriteLine(
                $"NodeTreeSnapshot: Cycle detected. " +
                $"{nodeCount - sorted.Count} node(s) in cycle(s) were skipped.");
        }

        // リソースとコンテキストを構築
        var nodeToResourceIndex = BuildResourcesAndContexts(sorted, renderer);

        // ConnectionSnapshot を構築
        var connectionList = BuildConnectionSnapshots(model.AllConnections, nodeToResourceIndex);

        // _inputConnectionMap を構築（ListInputSocket の順序を反映）
        BuildInputConnectionMap(connectionList);

        // Resource を初期化
        InitializeResources();

        _isDirty = false;
    }

    private (Dictionary<Node, int> inDegree, Dictionary<Node, List<Node>> adjacency) BuildAdjacencyAndInDegree(
        ICoreList<Node> nodes)
    {
        int nodeCount = nodes.Count;
        var inDegree = new Dictionary<Node, int>(nodeCount);
        var adjacency = new Dictionary<Node, List<Node>>(nodeCount);

        foreach (Node node in nodes)
        {
            inDegree[node] = 0;
            adjacency[node] = [];
        }

        foreach (Node node in nodes)
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

        return (inDegree, adjacency);
    }

    private static List<Node> TopologicalSort(
        Dictionary<Node, int> inDegree,
        Dictionary<Node, List<Node>> adjacency)
    {
        var queue = new Queue<Node>();
        var sorted = new List<Node>(inDegree.Count);

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

        return sorted;
    }

    private Dictionary<Node, int> BuildResourcesAndContexts(List<Node> sorted, IRenderer renderer)
    {
        var nodeToResourceIndex = new Dictionary<Node, int>(sorted.Count);
        _resources = new Node.Resource[sorted.Count];
        _contexts = new NodeRenderContext[sorted.Count];

        for (int i = 0; i < sorted.Count; i++)
        {
            Node node = sorted[i];
            nodeToResourceIndex[node] = i;

            // ItemValues を構築
            var itemIndexMap = new Dictionary<INodeItem, int>(node.Items.Count);
            var itemValues = new IItemValue[node.Items.Count];

            for (int j = 0; j < node.Items.Count; j++)
            {
                INodeItem item = node.Items[j];
                itemIndexMap[item] = j;
                itemValues[j] = item.CreateItemValue();
            }

            // Resource を生成
            var resource = node.ToResource(RenderContext.Default);
            resource.SlotIndex = i;
            resource.ItemValues = itemValues;
            resource.ItemIndexMap = itemIndexMap;
            resource.Renderer = renderer;
            _resources[i] = resource;

            _contexts[i] = new NodeRenderContext(renderer.Time) { Resource = resource, Snapshot = this };
        }

        return nodeToResourceIndex;
    }

    private List<ConnectionSnapshot> BuildConnectionSnapshots(
        IEnumerable<Connection> allConnections,
        Dictionary<Node, int> nodeToResourceIndex)
    {
        _outputConnectionMap.Clear();
        _connectedInputs.Clear();

        var connectionList = new List<ConnectionSnapshot>();

        foreach (Connection connection in allConnections)
        {
            if (connection.Output.Value is not { } outputSock
                || connection.Input.Value is not { } inputSock)
                continue;

            Node? outputNode = outputSock.FindHierarchicalParent<Node>();
            Node? inputNode = inputSock.FindHierarchicalParent<Node>();

            if (outputNode == null || inputNode == null
                                   || !nodeToResourceIndex.TryGetValue(outputNode, out int outputResourceIdx)
                                   || !nodeToResourceIndex.TryGetValue(inputNode, out int inputResourceIdx))
                continue;

            var outputResource = _resources[outputResourceIdx];
            var inputResource = _resources[inputResourceIdx];

            if (!outputResource.ItemIndexMap.TryGetValue((INodeItem)outputSock, out int outputItemIdx)
                || !inputResource.ItemIndexMap.TryGetValue((INodeItem)inputSock, out int inputItemIdx))
                continue;

            int connIdx = connectionList.Count;
            connectionList.Add(new ConnectionSnapshot
            {
                OutputSlotIndex = outputResourceIdx,
                OutputItemIndex = outputItemIdx,
                InputSlotIndex = inputResourceIdx,
                InputItemIndex = inputItemIdx,
                OriginalConnection = connection
            });

            // 出力→接続マップに追加
            var key = (outputResourceIdx, outputItemIdx);
            if (!_outputConnectionMap.TryGetValue(key, out var connIndices))
            {
                connIndices = [];
                _outputConnectionMap[key] = connIndices;
            }

            connIndices.Add(connIdx);

            // 接続済み入力セットに追加
            _connectedInputs.Add((inputResourceIdx, inputItemIdx));
        }

        _connections = connectionList.ToArray();
        return connectionList;
    }

    private void BuildInputConnectionMap(List<ConnectionSnapshot> connectionList)
    {
        _inputConnectionMap.Clear();

        // Connection -> connectionIndex のマッピングを作成
        var connectionToIndex = new Dictionary<Connection, int>();
        for (int i = 0; i < connectionList.Count; i++)
        {
            var conn = connectionList[i];
            if (conn.OriginalConnection != null)
            {
                connectionToIndex[conn.OriginalConnection] = i;
            }
        }

        // 各 ListInputSocket について、Connections の順序で登録
        for (int resourceIdx = 0; resourceIdx < _resources.Length; resourceIdx++)
        {
            var node = _resources[resourceIdx].GetOriginal();
            for (int itemIdx = 0; itemIdx < node.Items.Count; itemIdx++)
            {
                var item = node.Items[itemIdx];
                if (item is IListInputSocket listSocket)
                {
                    var key = (resourceIdx, itemIdx);
                    var orderedIndices = new List<int>();

                    foreach (var connRef in listSocket.Connections)
                    {
                        if (connRef.Value != null
                            && connectionToIndex.TryGetValue(connRef.Value, out int connIdx))
                        {
                            orderedIndices.Add(connIdx);
                        }
                    }

                    if (orderedIndices.Count > 0)
                    {
                        _inputConnectionMap[key] = orderedIndices;
                    }
                }
            }
        }
    }

    private void InitializeResources()
    {
        for (int i = 0; i < _resources.Length; i++)
        {
            _resources[i].BindSocketValues();
            _resources[i].Initialize(_contexts[i]);
        }
    }

    public void Evaluate(EvaluationTarget target, IList<EngineObject> renderables)
    {
        if (_renderer == null) return;

        foreach (var ctx in _contexts)
        {
            ctx.Target = target;
            ctx._renderables = renderables;
            ctx.Time = _renderer.Time;

            // アニメーション/プロパティ値をロード
            LoadAnimatedValues(ctx.Resource, ctx.Time);

            // ノード固有の評価
            ctx.Resource.Update(ctx);

            // 出力値を下流に伝搬
            PropagateOutputs(ctx.Resource);
        }
    }

    internal int FindSlotIndex(Node? node)
    {
        if (node == null) return -1;
        for (int i = 0; i < _resources.Length; i++)
        {
            if (_resources[i].GetOriginal() == node) return i;
        }

        return -1;
    }

    internal Node.Resource? GetResource(int slotIndex)
        => slotIndex >= 0 && slotIndex < _resources.Length ? _resources[slotIndex] : null;

    internal IItemValue? GetItemValue(int slotIndex, int itemIndex)
    {
        if (slotIndex < 0 || slotIndex >= _resources.Length) return null;
        var resource = _resources[slotIndex];
        if (itemIndex < 0 || itemIndex >= resource.ItemValues.Length) return null;
        return resource.ItemValues[itemIndex];
    }

    internal bool HasInputConnection(int slotIndex, int itemIndex)
        => _connectedInputs.Contains((slotIndex, itemIndex));

    internal void CollectListInputValues<T>(int slotIndex, int itemIndex, IList<T?> result)
    {
        var key = (slotIndex, itemIndex);

        // _inputConnectionMap を使用して ListInputSocket.Connections の順序を保持
        if (_inputConnectionMap.TryGetValue(key, out var connIndices))
        {
            foreach (int connIdx in connIndices)
            {
                ref var conn = ref _connections[connIdx];
                IItemValue outputValue = _resources[conn.OutputSlotIndex].ItemValues[conn.OutputItemIndex];
                if (outputValue is IReadOnlyItemValue<T> typed)
                {
                    result.Add(typed.GetValue());
                }
                else
                {
                    result.Add((T?)outputValue.GetBoxed());
                }
            }
        }
    }

    private void LoadAnimatedValues(Node.Resource resource, TimeSpan time)
    {
        var node = resource.GetOriginal();
        for (int i = 0; i < node.Items.Count; i++)
        {
            INodeItem item = node.Items[i];
            // 接続がある入力は上流から値が来るのでスキップ
            if (_connectedInputs.Contains((resource.SlotIndex, i))) continue;
            if (item.Property is null || item is IEnginePropertyBackedInputSocket) continue;

            if (item.Property is IAnimatablePropertyAdapter { Animation: { } animation })
            {
                if (!animation.UseGlobalClock)
                {
                    resource.ItemValues[i].TryLoadFromAnimation(animation, time - node.Start);
                }
                else
                {
                    resource.ItemValues[i].TryLoadFromAnimation(animation, time);
                }
            }
            else
            {
                resource.ItemValues[i].TryCopyFrom(item.Property);
            }
        }
    }

    private void PropagateOutputs(Node.Resource resource)
    {
        var node = resource.GetOriginal();
        for (int itemIdx = 0; itemIdx < node.Items.Count; itemIdx++)
        {
            if (!_outputConnectionMap.TryGetValue((resource.SlotIndex, itemIdx), out var connIndices))
                continue;

            IItemValue outputValue = resource.ItemValues[itemIdx];
            foreach (int ci in CollectionsMarshal.AsSpan(connIndices))
            {
                ref var conn = ref _connections[ci];
                IItemValue inputValue = _resources[conn.InputSlotIndex].ItemValues[conn.InputItemIndex];

                var propagateResult = inputValue.PropagateFrom(outputValue);
                conn.OriginalConnection?.Status = propagateResult switch
                {
                    PropagateResult.Success => ConnectionStatus.Success,
                    PropagateResult.Converted => ConnectionStatus.Convert,
                    _ => ConnectionStatus.Error
                };

                // IEnginePropertyBackedInputSocket なら、ItemValue の値をプロパティに反映
                if (conn is { OriginalConnection.Input.Value: IEnginePropertyBackedInputSocket inputSocket })
                {
                    inputSocket.CopyFrom(inputValue);
                }
            }
        }
    }

    private void Uninitialize()
    {
        foreach (var resource in _resources)
        {
            resource.Uninitialize();
            resource.Dispose();

            foreach (var itemValue in resource.ItemValues)
            {
                itemValue.Dispose();
            }
        }

        _resources = [];
        _contexts = [];
        _connections = [];
    }

    public void Dispose()
    {
        Uninitialize();
        _outputConnectionMap.Clear();
        _connectedInputs.Clear();
    }
}
