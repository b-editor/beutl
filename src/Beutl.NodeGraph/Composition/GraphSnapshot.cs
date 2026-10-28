using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Beutl.Collections;
using Beutl.Composition;
using Beutl.Extensibility;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.NodeGraph.Nodes;
using Microsoft.Extensions.Logging;

namespace Beutl.NodeGraph.Composition;

public sealed class GraphSnapshot : IDisposable
{
    private GraphNode.Resource[] _resources = [];
    private GraphCompositionContext[] _contexts = [];
    private ConnectionSnapshot[] _connections = [];
    // The statuses of _connections that evaluation found, and those last posted to the document thread.
    private ConnectionStatus[] _statuses = [];
    private ConnectionStatus[] _postedStatuses = [];
    private readonly Dictionary<(int, int), List<int>> _outputConnectionMap = new();
    private readonly Dictionary<(int, int), List<int>> _inputConnectionMap = new();
    private readonly HashSet<(int, int)> _connectedInputs = [];
    private bool _isDirty = true;
    private int _resourceCount;
    private int _initializedResourceCount;
    private int _lifecycleInProgress;

    // The app posts the Connection.Status writes to the UI thread, which owns the document (#2873).
    internal static Action<Action>? PostToDocumentThread { get; set; }

    public void MarkDirty() => _isDirty = true;

    public void Build(GraphModel model, CompositionContext context)
    {
        if (Interlocked.CompareExchange(ref _lifecycleInProgress, 1, 0) != 0)
            throw new InvalidOperationException("A graph snapshot lifecycle operation is already in progress.");
        try
        {
            BuildCore(model, context);
        }
        finally
        {
            Interlocked.Exchange(ref _lifecycleInProgress, 0);
        }
    }

    private void BuildCore(GraphModel model, CompositionContext context)
    {
        foreach (GraphNode node in model.Nodes)
            _ = node.NestedInputPorts;
        if (!_isDirty) return;

        Uninitialize();

        // Rejected edges retain their connections, but must not leave an old expression value
        // overriding the property's local value or animation. Restore valid expressions before
        // creating resources so recovered connections can supply their values again.
        foreach (GraphNode node in model.Nodes)
        {
            foreach (IInputPort input in node.GetConnectedInputs())
            {
                if (input is IConnectionExpressionController expression)
                    expression.UpdateExpression(node.CanConnectInput(input));
            }
        }

        int nodeCount = model.Nodes.Count;
        if (nodeCount == 0)
        {
            _isDirty = false;
            return;
        }

        var (inDegree, adjacency) = BuildAdjacencyAndInDegree(model.Nodes);

        var sorted = TopologicalSort(inDegree, adjacency);

        if (sorted.Count != nodeCount)
        {
            Log.CreateLogger<GraphSnapshot>().LogWarning("Node graph contains a cycle; {Count} blocked nodes were skipped.", nodeCount - sorted.Count);
        }

        try
        {
            var nodeToResourceIndex = BuildResourcesAndContexts(sorted, context);

            var connectionList = BuildConnectionSnapshots(model.AllConnections, nodeToResourceIndex);

            BuildInputConnectionMap(connectionList);

            InitializeResources();

            _isDirty = false;
        }
        catch (Exception ex)
        {
            Uninitialize(ExceptionDispatchInfo.Capture(ex));
            throw;
        }
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
            foreach (INodeMember item in node.EnumerateMembers())
            {
                // Retained invalid inputs must not create dependencies that block valid nodes.
                if (item is IInputPort input && !node.CanConnectInput(input)) continue;
                if (item is IListInputPort listInputPort)
                {
                    foreach (var connection in listInputPort.Connections)
                    {
                        AddEdge(
                            connection.Value?.Output.Value.Value?.FindHierarchicalParent<GraphNode>(),
                            node);
                    }
                }
                else if (item is IInputPort inputNodePort
                         && inputNodePort.Connection.Value.Value?.Output.Value.Value is { } outputNodePort)
                {
                    AddEdge(outputNodePort.FindHierarchicalParent<GraphNode>(), node);
                }
            }
        }

        return (inDegree, adjacency);

        void AddEdge(GraphNode? upstream, GraphNode downstream)
        {
            if (upstream != null && inDegree.ContainsKey(upstream))
            {
                adjacency[upstream].Add(downstream);
                inDegree[downstream]++;
            }
        }
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

            INodeMember[] members = node.EnumerateMembers().ToArray();
            var itemIndexMap = new Dictionary<INodeMember, int>(members.Length);
            var itemValues = new IItemValue[members.Length];
            int itemValueCount = 0;
            GraphNode.Resource resource;
            try
            {
                for (int j = 0; j < members.Length; j++)
                {
                    INodeMember item = members[j];
                    itemIndexMap[item] = j;
                    itemValues[j] = item.CreateItemValue();
                    itemValueCount++;
                }

                resource = node.ToResource(context);
            }
            catch
            {
                ExceptionDispatchInfo? cleanupFailure = null;
                DisposeItemValues(itemValues, itemValueCount, ref cleanupFailure);
                throw;
            }

            resource.SlotIndex = i;
            resource.ItemValues = itemValues;
            resource.Members = members;
            resource.ItemIndexMap = itemIndexMap;
            _resources[i] = resource;
            _resourceCount++;

            _contexts[i] = new GraphCompositionContext(context.Time)
            {
                Resource = resource,
                Snapshot = this,
                DisableResourceShare = context.DisableResourceShare,
                Conditions = context.Conditions,
            };
        }

        return nodeToResourceIndex;
    }

    private List<ConnectionSnapshot> BuildConnectionSnapshots(
        IEnumerable<Connection> allConnections,
        Dictionary<GraphNode, int> nodeToResourceIndex)
    {
        var connectionList = new List<ConnectionSnapshot>();
        List<(Connection, ConnectionStatus)>? rejected = null;

        foreach (Connection connection in allConnections)
        {
            if (connection.Output.Value.Value is not { } outputSock
                || connection.Input.Value.Value is not { } inputSock)
                continue;

            GraphNode? outputNode = outputSock.FindHierarchicalParent<GraphNode>();
            GraphNode? inputNode = inputSock.FindHierarchicalParent<GraphNode>();

            if (outputNode == null || inputNode == null
                                   || !nodeToResourceIndex.TryGetValue(outputNode, out int outputResourceIdx)
                                   || !nodeToResourceIndex.TryGetValue(inputNode, out int inputResourceIdx))
            {
                (rejected ??= []).Add((connection, ConnectionStatus.Error));
                continue;
            }

            if (inputSock is IInputPort input && !inputNode.CanConnectInput(input))
            {
                (rejected ??= []).Add((connection, ConnectionStatus.Error));
                continue;
            }

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

            var key = (outputResourceIdx, outputItemIdx);
            if (!_outputConnectionMap.TryGetValue(key, out var connIndices))
            {
                connIndices = [];
                _outputConnectionMap[key] = connIndices;
            }

            connIndices.Add(connIdx);

            _connectedInputs.Add((inputResourceIdx, inputItemIdx));
        }

        _connections = connectionList.ToArray();
        _statuses = new ConnectionStatus[_connections.Length];
        _postedStatuses = new ConnectionStatus[_connections.Length];
        if (rejected != null)
            PostStatuses(rejected);
        return connectionList;
    }

    private void BuildInputConnectionMap(List<ConnectionSnapshot> connectionList)
    {
        var connectionToIndex = new Dictionary<Connection, int>();
        for (int i = 0; i < connectionList.Count; i++)
        {
            connectionToIndex[connectionList[i].OriginalConnection] = i;
        }

        for (int resourceIdx = 0; resourceIdx < _resources.Length; resourceIdx++)
        {
            GraphNode node = _resources[resourceIdx].RequireOriginal();
            for (int itemIdx = 0; itemIdx < _resources[resourceIdx].Members.Length; itemIdx++)
            {
                var item = _resources[resourceIdx].Members[itemIdx];
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
            // Initialize may subscribe before throwing, so its matching cleanup is still needed.
            _initializedResourceCount = i + 1;
            _resources[i].Initialize(_contexts[i]);
        }
    }

    public void Evaluate(CompositionContext context)
    {
        foreach (var ctx in _contexts)
        {
            ctx.Time = context.Time;
            ctx.DisableResourceShare = context.DisableResourceShare;
            ctx.Conditions = context.Conditions;

            LoadAnimatedValues(ctx.Resource, ctx.Time);

            ctx.Resource.Update(ctx);

            PropagateOutputs(ctx.Resource);
        }

        PostChangedStatuses();
    }

    // Adds the render node each OutputNode of model received in the last evaluation.
    internal void CollectOutputRenderNodes(GraphModel model, List<RenderNode> result)
    {
        foreach (var node in model.Nodes)
        {
            if (node is OutputNode outputNode)
            {
                int slotIndex = FindSlotIndex(outputNode);
                if (slotIndex < 0) continue;

                var resource = GetResource(slotIndex);
                if (resource == null) continue;

                if (!resource.ItemIndexMap.TryGetValue(outputNode.InputPort, out int itemIndex))
                    continue;

                IItemValue? itemValue = GetItemValue(slotIndex, itemIndex);
                if (itemValue?.GetBoxed() is RenderNode renderNode)
                {
                    result.Add(renderNode);
                }
            }
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

    internal GraphCompositionContext? GetContext(int slotIndex)
        => slotIndex >= 0 && slotIndex < _contexts.Length ? _contexts[slotIndex] : null;

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

        if (_inputConnectionMap.TryGetValue(key, out var connIndices))
        {
            foreach (int connIdx in connIndices)
            {
                ref var conn = ref _connections[connIdx];
                IItemValue outputValue = _resources[conn.OutputSlotIndex].ItemValues[conn.OutputItemIndex];
                if (outputValue is IReadOnlyItemValue<T> typed)
                {
                    result.Add(typed.GetValue());
                    _statuses[connIdx] = ConnectionStatus.Success;
                }
                else if (outputValue.GetBoxed() is T unboxed)
                {
                    result.Add(unboxed);
                    _statuses[connIdx] = ConnectionStatus.Convert;
                }
                else
                {
                    _statuses[connIdx] = ConnectionStatus.Error;
                }
            }
        }
    }

    private void LoadAnimatedValues(GraphNode.Resource resource, MediaTime time)
    {
        GraphNode node = resource.RequireOriginal();
        for (int i = 0; i < resource.Members.Length; i++)
        {
            INodeMember item = resource.Members[i];
            // 接続がある入力は上流から値が来るのでスキップ
            if (_connectedInputs.Contains((resource.SlotIndex, i))) continue;
            if (item.Property is null || item is IEnginePropertyBackedInputPort) continue;

            if (item.Property is IAnimatablePropertyAdapter { Animation: { } animation })
            {
                if (!animation.UseGlobalClock.Value)
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
        GraphNode node = resource.RequireOriginal();
        for (int itemIdx = 0; itemIdx < resource.Members.Length; itemIdx++)
        {
            if (!_outputConnectionMap.TryGetValue((resource.SlotIndex, itemIdx), out var connIndices))
                continue;

            IItemValue outputValue = resource.ItemValues[itemIdx];
            foreach (int ci in CollectionsMarshal.AsSpan(connIndices))
            {
                ref var conn = ref _connections[ci];
                IItemValue inputValue = _resources[conn.InputSlotIndex].ItemValues[conn.InputItemIndex];

                var propagateResult = inputValue.PropagateFrom(outputValue);
                _statuses[ci] = propagateResult switch
                {
                    PropagateResult.Success => ConnectionStatus.Success,
                    PropagateResult.Converted => ConnectionStatus.Convert,
                    _ => ConnectionStatus.Error
                };

                if (conn is { OriginalConnection.Input.Value.Value: IEnginePropertyBackedInputPort inputNodePort })
                {
                    inputNodePort.CopyFrom(inputValue);
                }
            }
        }
    }

    private void PostChangedStatuses()
    {
        List<(Connection, ConnectionStatus)>? changed = null;
        for (int i = 0; i < _statuses.Length; i++)
        {
            ConnectionStatus status = _statuses[i];
            // Zero is a connection that no evaluation has reached yet.
            if (status == 0 || status == _postedStatuses[i]) continue;

            _postedStatuses[i] = status;
            (changed ??= []).Add((_connections[i].OriginalConnection, status));
        }

        if (changed != null)
            PostStatuses(changed);
    }

    private static void PostStatuses(List<(Connection Connection, ConnectionStatus Status)> statuses)
    {
        void Write()
        {
            foreach ((Connection connection, ConnectionStatus status) in statuses)
            {
                // A connection removed meanwhile keeps the Disconnected that its removal set.
                if (connection.HierarchicalParent != null)
                    connection.Status.Value = status;
            }
        }

        if (PostToDocumentThread is { } post)
            post(Write);
        else
            Write();
    }

    private static void DisposeItemValues(IItemValue[] itemValues, int count, ref ExceptionDispatchInfo? failure)
    {
        for (int i = 0; i < count; i++)
        {
            try
            {
                itemValues[i].Dispose();
            }
            catch (Exception ex)
            {
                failure ??= ExceptionDispatchInfo.Capture(ex);
            }
        }
    }

    private void Uninitialize(ExceptionDispatchInfo? failure = null)
    {
        GraphNode.Resource[] resources = _resources;
        int resourceCount = _resourceCount;
        int initializedResourceCount = _initializedResourceCount;

        // Release the snapshot's ownership first so a failed or reentrant cleanup cannot repeat it.
        _resources = [];
        _contexts = [];
        _connections = [];
        _statuses = [];
        _postedStatuses = [];
        _resourceCount = 0;
        _initializedResourceCount = 0;
        _isDirty = true;

        _outputConnectionMap.Clear();
        _connectedInputs.Clear();
        _inputConnectionMap.Clear();

        for (int i = 0; i < resourceCount; i++)
        {
            GraphNode.Resource resource = resources[i];
            if (i < initializedResourceCount)
            {
                try
                {
                    resource.Uninitialize();
                }
                catch (Exception ex)
                {
                    failure ??= ExceptionDispatchInfo.Capture(ex);
                }
            }

            try
            {
                resource.Dispose();
            }
            catch (Exception ex)
            {
                failure ??= ExceptionDispatchInfo.Capture(ex);
            }

            DisposeItemValues(resource.ItemValues, resource.ItemValues.Length, ref failure);
        }

        failure?.Throw();
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _lifecycleInProgress, 1, 0) != 0)
            throw new InvalidOperationException("A graph snapshot lifecycle operation is already in progress.");
        try
        {
            Uninitialize();
            _outputConnectionMap.Clear();
            _connectedInputs.Clear();
        }
        finally
        {
            Interlocked.Exchange(ref _lifecycleInProgress, 0);
        }
    }
}
