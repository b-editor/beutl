using System.Diagnostics;
using System.Runtime.InteropServices;
using Beutl.Collections;
using Beutl.Composition;
using Beutl.Extensibility;

namespace Beutl.NodeGraph.Composition;

public sealed class GraphSnapshot : IDisposable
{
    private GraphNode.Resource[] _resources = [];
    private GraphCompositionContext[] _contexts = [];
    private ConnectionSnapshot[] _connections = [];
    private readonly Dictionary<(int, int), List<int>> _outputConnectionMap = new();
    private readonly Dictionary<(int, int), List<int>> _inputConnectionMap = new();
    private readonly HashSet<(int, int)> _connectedInputs = [];
    private bool _isDirty = true;

    public void MarkDirty() => _isDirty = true;

    public void Build(GraphModel model, CompositionContext context)
    {
        if (!_isDirty) return;

        // 既存リソースをクリーンアップ
        Uninitialize();

        int nodeCount = model.Nodes.Count;
        if (nodeCount == 0)
        {
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
                $"NodeGraphSnapshot: Cycle detected. " +
                $"{nodeCount - sorted.Count} node(s) in cycle(s) were skipped.");
        }

        // リソースとコンテキストを構築
        var nodeToResourceIndex = BuildResourcesAndContexts(sorted, context);

        // ConnectionSnapshot を構築
        var connectionList = BuildConnectionSnapshots(model.AllConnections, nodeToResourceIndex);

        // _inputConnectionMap を構築（ListInputPort の順序を反映）
        BuildInputConnectionMap(connectionList);

        // Resource を初期化
        InitializeResources();

        _isDirty = false;
    }

    private (Dictionary<GraphNode, int> inDegree, Dictionary<GraphNode, List<GraphNode>> adjacency) BuildAdjacencyAndInDegree(
        ICoreList<GraphNode> nodes)
    {
        int nodeCount = nodes.Count;
        var inDegree = new Dictionary<GraphNode, int>(nodeCount);
        var adjacency = new Dictionary<GraphNode, List<GraphNode>>(nodeCount);

        foreach (GraphNode node in nodes)
        {
            inDegree[node] = 0;
            adjacency[node] = [];
        }

        foreach (GraphNode node in nodes)
        {
            for (int i = 0; i < node.Items.Count; i++)
            {
                INodeMember item = node.Items[i];
                if (item is IListInputPort listInputPort)
                {
                    foreach (var connection in listInputPort.Connections)
                    {
                        GraphNode? upstream = connection.Value?.Output.Value?
                            .FindHierarchicalParent<GraphNode>();
                        if (upstream != null && inDegree.ContainsKey(upstream))
                        {
                            adjacency[upstream].Add(node);
                            inDegree[node]++;
                        }
                    }
                }
                else if (item is IInputPort inputNodePort
                         && inputNodePort.Connection.Value?.Output.Value is { } outputNodePort)
                {
                    GraphNode? upstream = outputNodePort.FindHierarchicalParent<GraphNode>();
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

    private static List<GraphNode> TopologicalSort(
        Dictionary<GraphNode, int> inDegree,
        Dictionary<GraphNode, List<GraphNode>> adjacency)
    {
        var queue = new Queue<GraphNode>();
        var sorted = new List<GraphNode>(inDegree.Count);

        foreach (var (node, degree) in inDegree)
        {
            if (degree == 0)
                queue.Enqueue(node);
        }

        while (queue.Count > 0)
        {
            GraphNode current = queue.Dequeue();
            sorted.Add(current);

            foreach (GraphNode downstream in adjacency[current])
            {
                if (--inDegree[downstream] == 0)
                    queue.Enqueue(downstream);
            }
        }

        return sorted;
    }

    private Dictionary<GraphNode, int> BuildResourcesAndContexts(List<GraphNode> sorted, CompositionContext context)
    {
        var nodeToResourceIndex = new Dictionary<GraphNode, int>(sorted.Count);
        _resources = new GraphNode.Resource[sorted.Count];
        _contexts = new GraphCompositionContext[sorted.Count];

        for (int i = 0; i < sorted.Count; i++)
        {
            GraphNode node = sorted[i];
            nodeToResourceIndex[node] = i;

            // ItemValues を構築
            var itemIndexMap = new Dictionary<INodeMember, int>(node.Items.Count);
            var itemValues = new IItemValue[node.Items.Count];

            for (int j = 0; j < node.Items.Count; j++)
            {
                INodeMember item = node.Items[j];
                itemIndexMap[item] = j;
                itemValues[j] = item.CreateItemValue();
            }

            // Resource を生成
            var resource = node.ToResource(context);
            resource.SlotIndex = i;
            resource.ItemValues = itemValues;
            resource.ItemIndexMap = itemIndexMap;
            _resources[i] = resource;

            _contexts[i] = new GraphCompositionContext(context.Time)
            {
                Resource = resource,
                Snapshot = this,
                DisableResourceShare = context.DisableResourceShare,
            };
        }

        return nodeToResourceIndex;
    }

    private List<ConnectionSnapshot> BuildConnectionSnapshots(
        IEnumerable<Connection> allConnections,
        Dictionary<GraphNode, int> nodeToResourceIndex)
    {
        var connectionList = new List<ConnectionSnapshot>();

        foreach (Connection connection in allConnections)
        {
            if (connection.Output.Value is not { } outputSock
                || connection.Input.Value is not { } inputSock)
                continue;

            GraphNode? outputNode = outputSock.FindHierarchicalParent<GraphNode>();
            GraphNode? inputNode = inputSock.FindHierarchicalParent<GraphNode>();

            if (outputNode == null || inputNode == null
                                   || !nodeToResourceIndex.TryGetValue(outputNode, out int outputResourceIdx)
                                   || !nodeToResourceIndex.TryGetValue(inputNode, out int inputResourceIdx))
                continue;

            var outputResource = _resources[outputResourceIdx];
            var inputResource = _resources[inputResourceIdx];

            if (!outputResource.ItemIndexMap.TryGetValue((INodeMember)outputSock, out int outputItemIdx)
                || !inputResource.ItemIndexMap.TryGetValue((INodeMember)inputSock, out int inputItemIdx))
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

        // 各 ListInputPort について、Connections の順序で登録
        for (int resourceIdx = 0; resourceIdx < _resources.Length; resourceIdx++)
        {
            var node = _resources[resourceIdx].GetOriginal();
            for (int itemIdx = 0; itemIdx < node.Items.Count; itemIdx++)
            {
                var item = node.Items[itemIdx];
                if (item is IListInputPort listNodePort)
                {
                    var key = (resourceIdx, itemIdx);
                    var orderedIndices = new List<int>();

                    foreach (var connRef in listNodePort.Connections)
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
            _resources[i].BindNodePortValues();
            _resources[i].Initialize(_contexts[i]);
        }
    }

    public void Evaluate(CompositionTarget target, CompositionContext context)
    {
        foreach (var ctx in _contexts)
        {
            ctx.Target = target;
            ctx.Time = context.Time;

            // アニメーション/プロパティ値をロード
            LoadAnimatedValues(ctx.Resource, ctx.Time);

            // ノード固有の評価
            ctx.Resource.Update(ctx);

            // 出力値を下流に伝搬
            PropagateOutputs(ctx.Resource);
        }
    }

    internal int FindSlotIndex(GraphNode? node)
    {
        if (node == null) return -1;
        for (int i = 0; i < _resources.Length; i++)
        {
            if (_resources[i].GetOriginal() == node) return i;
        }

        return -1;
    }

    internal GraphNode.Resource? GetResource(int slotIndex)
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

        // _inputConnectionMap を使用して ListInputPort.Connections の順序を保持
        if (_inputConnectionMap.TryGetValue(key, out var connIndices))
        {
            foreach (int connIdx in connIndices)
            {
                ref var conn = ref _connections[connIdx];
                IItemValue outputValue = _resources[conn.OutputSlotIndex].ItemValues[conn.OutputItemIndex];
                if (outputValue is IReadOnlyItemValue<T> typed)
                {
                    result.Add(typed.GetValue());
                    conn.OriginalConnection?.Status = ConnectionStatus.Success;
                }
                else if (outputValue.GetBoxed() is T unboxed)
                {
                    result.Add(unboxed);
                    conn.OriginalConnection?.Status = ConnectionStatus.Convert;
                }
                else
                {
                    conn.OriginalConnection?.Status = ConnectionStatus.Error;
                }
            }
        }
    }

    private void LoadAnimatedValues(GraphNode.Resource resource, TimeSpan time)
    {
        var node = resource.GetOriginal();
        for (int i = 0; i < node.Items.Count; i++)
        {
            INodeMember item = node.Items[i];
            // 接続がある入力は上流から値が来るのでスキップ
            if (_connectedInputs.Contains((resource.SlotIndex, i))) continue;
            if (item.Property is null || item is IEnginePropertyBackedInputPort) continue;

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

    private void PropagateOutputs(GraphNode.Resource resource)
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

                // IEnginePropertyBackedInputPort なら、ItemValue の値をプロパティに反映
                if (conn is { OriginalConnection.Input.Value: IEnginePropertyBackedInputPort inputNodePort })
                {
                    inputNodePort.CopyFrom(inputValue);
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

        _outputConnectionMap.Clear();
        _connectedInputs.Clear();
        _inputConnectionMap.Clear();
    }

    public void Dispose()
    {
        Uninitialize();
        _outputConnectionMap.Clear();
        _connectedInputs.Clear();
    }
}
