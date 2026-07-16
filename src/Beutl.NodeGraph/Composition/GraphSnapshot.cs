using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Beutl.Collections;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Extensibility;

namespace Beutl.NodeGraph.Composition;

public sealed class GraphSnapshot : IDisposable
{
    private readonly object _lifecycleGate = new();
    private GraphNode.Resource[] _resources = [];
    private GraphCompositionContext[] _contexts = [];
    private ConnectionSnapshot[] _connections = [];
    private IItemValue[] _itemValues = [];
    private bool[] _initializationAttempted = [];
    private Dictionary<(int, int), List<int>> _outputConnectionMap = new();
    private Dictionary<(int, int), List<int>> _inputConnectionMap = new();
    private HashSet<(int, int)> _connectedInputs = [];
    private bool _isDirty = true;
    private int _dirtyRequested;
    private int _operationOwnerThreadId;
    private int _operationDepth;
    private int _cleanupOwnerThreadId;
    private bool _cleanupReserved;

    public void MarkDirty()
    {
        // Topology notifications can arrive from the editing thread while this snapshot is being evaluated on a
        // render thread. They are invalidation signals rather than state access, so recording one must never be
        // rejected by the operation gate. Build consumes only the request present at entry; a notification that
        // arrives during the rebuild remains pending for the next call instead of being overwritten at commit.
        Interlocked.Exchange(ref _dirtyRequested, 1);
    }

    /// <summary>Builds the graph resources from the supplied ambient composition state.</summary>
    public void Build(GraphModel model, CompositionContext context)
    {
        using SnapshotOperationLease operation = BeginOperation();
        bool dirtyRequested = Interlocked.Exchange(ref _dirtyRequested, 0) != 0;
        if (!_isDirty && !dirtyRequested) return;

        bool buildCompleted = false;
        try
        {
            // Clean up the previously built resources.
            Uninitialize(allowCurrentOperation: true);

            int nodeCount = model.Nodes.Count;
            if (nodeCount == 0)
            {
                buildCompleted = true;
                _isDirty = false;
                return;
            }

            // Build the adjacency list and in-degree counts.
            var (inDegree, adjacency) = BuildAdjacencyAndInDegree(model.Nodes);

            // Run Kahn's breadth-first topological sort.
            var sorted = TopologicalSort(inDegree, adjacency);

            // Detect cycles.
            if (sorted.Count != nodeCount)
            {
                Debug.WriteLine(
                    $"NodeGraphSnapshot: Cycle detected. " +
                    $"{nodeCount - sorted.Count} node(s) in cycle(s) were skipped.");
            }

            var acquiredResources = new List<GraphNode.Resource>(sorted.Count);
            var acquiredItemValues = new List<IItemValue>();
            bool stateInstalled = false;
            try
            {
                // Build every fallible part against local state first. Only initialization needs the snapshot installed:
                // GraphNode.Initialize is allowed to query its owning snapshot (GroupNode does this recursively).
                var (nodeToResourceIndex, resources, contexts) = BuildResourcesAndContexts(
                    sorted, context, acquiredResources, acquiredItemValues);
                var outputConnectionMap = new Dictionary<(int, int), List<int>>();
                var connectedInputs = new HashSet<(int, int)>();
                var connectionList = BuildConnectionSnapshots(
                    model.AllConnections,
                    nodeToResourceIndex,
                    resources,
                    outputConnectionMap,
                    connectedInputs);
                var inputConnectionMap = new Dictionary<(int, int), List<int>>();
                BuildInputConnectionMap(connectionList, resources, inputConnectionMap);

                InstallState(
                    resources,
                    contexts,
                    connectionList.ToArray(),
                    acquiredItemValues.ToArray(),
                    new bool[resources.Length],
                    outputConnectionMap,
                    inputConnectionMap,
                    connectedInputs);
                stateInstalled = true;

                InitializeResources();
                buildCompleted = true;
                _isDirty = false;
            }
            catch
            {
                if (!stateInstalled && (acquiredResources.Count != 0 || acquiredItemValues.Count != 0))
                {
                    // Publish incomplete acquisitions only to the private cleanup state. This lets the same graph-wide
                    // reservation used by normal disposal retain ownership when a resource is busy, instead of dropping
                    // a detached array whose best-effort Dispose call may be rejected.
                    InstallState(
                        acquiredResources.ToArray(),
                        [],
                        [],
                        acquiredItemValues.ToArray(),
                        new bool[acquiredResources.Count],
                        new Dictionary<(int, int), List<int>>(),
                        new Dictionary<(int, int), List<int>>(),
                        []);
                    stateInstalled = true;
                }

                if (stateInstalled)
                {
                    try
                    {
                        Uninitialize(allowCurrentOperation: true);
                    }
                    catch
                    {
                        // The build exception is primary. A post-reservation cleanup failure has already swept the
                        // graph; a pre-reservation failure leaves the complete installed state owned for Dispose/retry.
                    }
                }

                throw;
            }
        }
        finally
        {
            if (!buildCompleted)
                _isDirty = true;
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

    private (Dictionary<GraphNode, int> NodeToResourceIndex,
        GraphNode.Resource[] Resources,
        GraphCompositionContext[] Contexts) BuildResourcesAndContexts(
        List<GraphNode> sorted,
        CompositionContext context,
        List<GraphNode.Resource> acquiredResources,
        List<IItemValue> acquiredItemValues)
    {
        var nodeToResourceIndex = new Dictionary<GraphNode, int>(sorted.Count);
        var resources = new GraphNode.Resource[sorted.Count];
        var contexts = new GraphCompositionContext[sorted.Count];

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
                IItemValue itemValue = item.CreateItemValue();
                itemValues[j] = itemValue;
                acquiredItemValues.Add(itemValue);
            }

            // Resource を生成
            var resource = node.ToResource(context);
            acquiredResources.Add(resource);
            resource.InstallGraphState(i, itemValues, itemIndexMap);
            resources[i] = resource;

            contexts[i] = new GraphCompositionContext(context)
            {
                Resource = resource,
                Snapshot = this,
            };
        }

        return (nodeToResourceIndex, resources, contexts);
    }

    private List<ConnectionSnapshot> BuildConnectionSnapshots(
        IEnumerable<Connection> allConnections,
        Dictionary<GraphNode, int> nodeToResourceIndex,
        GraphNode.Resource[] resources,
        Dictionary<(int, int), List<int>> outputConnectionMap,
        HashSet<(int, int)> connectedInputs)
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

            var outputResource = resources[outputResourceIdx];
            var inputResource = resources[inputResourceIdx];

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
            if (!outputConnectionMap.TryGetValue(key, out var connIndices))
            {
                connIndices = [];
                outputConnectionMap[key] = connIndices;
            }

            connIndices.Add(connIdx);

            // 接続済み入力セットに追加
            connectedInputs.Add((inputResourceIdx, inputItemIdx));
        }

        return connectionList;
    }

    private void BuildInputConnectionMap(
        List<ConnectionSnapshot> connectionList,
        GraphNode.Resource[] resources,
        Dictionary<(int, int), List<int>> inputConnectionMap)
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
        for (int resourceIdx = 0; resourceIdx < resources.Length; resourceIdx++)
        {
            var node = resources[resourceIdx].GetOriginal();
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
                        inputConnectionMap[key] = orderedIndices;
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
            // Initialize may subscribe or allocate before throwing. Mark the attempt first so rollback invokes the
            // matching Uninitialize hook for this resource as well as every earlier successful resource.
            _initializationAttempted[i] = true;
            _resources[i].Initialize(_contexts[i]);
        }
    }

    /// <summary>Evaluates the graph using the supplied ambient composition state.</summary>
    public void Evaluate(CompositionTarget target, CompositionContext context)
    {
        using SnapshotOperationLease operation = BeginOperation();
        foreach (var ctx in _contexts)
        {
            ctx.Target = target;
            ctx.UpdateFrom(context);

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
        using SnapshotOperationLease operation = BeginOperation();
        if (node == null) return -1;
        for (int i = 0; i < _resources.Length; i++)
        {
            if (_resources[i].GetOriginal() == node) return i;
        }

        return -1;
    }

    internal GraphNode.Resource? GetResource(int slotIndex)
    {
        using SnapshotOperationLease operation = BeginOperation();
        return slotIndex >= 0 && slotIndex < _resources.Length ? _resources[slotIndex] : null;
    }

    internal bool HasOwnedState()
    {
        using SnapshotOperationLease operation = BeginOperation();
        return _resources.Length != 0 || _itemValues.Length != 0;
    }

    // Reserve is deliberately separate from detachment. If any child is busy, the shared cleanup context rolls the
    // complete ownership graph back while this snapshot still exposes the original coherent state.
    internal void ReserveResources(Action<GraphNode.Resource> reserve)
    {
        ArgumentNullException.ThrowIfNull(reserve);
        BeginCleanupReservation(allowCurrentOperation: false);

        try
        {
            foreach (GraphNode.Resource resource in _resources)
            {
                reserve(resource);
            }
        }
        catch
        {
            RollbackCleanupReservation();
            throw;
        }
    }

    internal void RollbackCleanupReservation()
    {
        lock (_lifecycleGate)
        {
            if (!_cleanupReserved)
                return;

            if (_cleanupOwnerThreadId != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("The graph cleanup reservation is owned by another thread.");

            _cleanupReserved = false;
            _cleanupOwnerThreadId = 0;
            Monitor.PulseAll(_lifecycleGate);
        }
    }

    internal IItemValue? GetItemValue(int slotIndex, int itemIndex)
    {
        using SnapshotOperationLease operation = BeginOperation();
        if (slotIndex < 0 || slotIndex >= _resources.Length) return null;
        var resource = _resources[slotIndex];
        if (itemIndex < 0 || itemIndex >= resource.ItemValues.Count) return null;
        return resource.ItemValues[itemIndex];
    }

    internal bool HasInputConnection(int slotIndex, int itemIndex)
    {
        using SnapshotOperationLease operation = BeginOperation();
        return _connectedInputs.Contains((slotIndex, itemIndex));
    }

    internal void CollectListInputValues<T>(int slotIndex, int itemIndex, IList<T?> result)
    {
        using SnapshotOperationLease operation = BeginOperation();
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

    private void InstallState(
        GraphNode.Resource[] resources,
        GraphCompositionContext[] contexts,
        ConnectionSnapshot[] connections,
        IItemValue[] itemValues,
        bool[] initializationAttempted,
        Dictionary<(int, int), List<int>> outputConnectionMap,
        Dictionary<(int, int), List<int>> inputConnectionMap,
        HashSet<(int, int)> connectedInputs)
    {
        _resources = resources;
        _contexts = contexts;
        _connections = connections;
        _itemValues = itemValues;
        _initializationAttempted = initializationAttempted;
        _outputConnectionMap = outputConnectionMap;
        _inputConnectionMap = inputConnectionMap;
        _connectedInputs = connectedInputs;
    }

    private DetachedState DetachState()
    {
        var state = new DetachedState(_resources, _itemValues, _initializationAttempted);

        _resources = [];
        _contexts = [];
        _connections = [];
        _itemValues = [];
        _initializationAttempted = [];
        _outputConnectionMap = new Dictionary<(int, int), List<int>>();
        _inputConnectionMap = new Dictionary<(int, int), List<int>>();
        _connectedInputs = [];
        _isDirty = true;
        return state;
    }

    private static Exception? CleanupDetachedState(DetachedState state)
    {
        Exception? failure = null;

        // Uninitialize only resources whose Initialize call was attempted. A Bind failure must not invoke an
        // unmatched Uninitialize hook, while an Initialize failure must (the hook may have partially subscribed).
        for (int i = 0; i < state.Resources.Length; i++)
        {
            if (i < state.InitializationAttempted.Length && state.InitializationAttempted[i])
            {
                GraphNode.Resource resource = state.Resources[i];
                NodeGraphDisposal.Capture(resource.Uninitialize, ref failure);
            }
        }

        foreach (GraphNode.Resource resource in state.Resources)
            NodeGraphDisposal.Capture(resource, ref failure);

        foreach (IItemValue itemValue in state.ItemValues)
            NodeGraphDisposal.Capture(itemValue, ref failure);

        return failure;
    }

    private void Uninitialize(bool allowCurrentOperation = false)
    {
        if (_resources.Length == 0 && _itemValues.Length == 0)
            return;

        new CleanupCoordinator(this, allowCurrentOperation).Dispose();
    }

    internal void DisposeAfterResourcesReserved(
        Action<GraphNode.Resource> disposeReservedResource,
        Action<Exception> captureFailure)
    {
        ArgumentNullException.ThrowIfNull(disposeReservedResource);
        ArgumentNullException.ThrowIfNull(captureFailure);

        ValidateCleanupReservation();
        try
        {
            DetachedState state = DetachState();
            for (int i = 0; i < state.Resources.Length; i++)
            {
                if (i < state.InitializationAttempted.Length && state.InitializationAttempted[i])
                {
                    try
                    {
                        state.Resources[i].UninitializeAfterResourcesReserved();
                    }
                    catch (Exception ex)
                    {
                        captureFailure(ex);
                    }
                }
            }

            foreach (GraphNode.Resource resource in state.Resources)
            {
                try
                {
                    disposeReservedResource(resource);
                }
                catch (Exception ex)
                {
                    captureFailure(ex);
                }
            }

            foreach (IItemValue itemValue in state.ItemValues)
            {
                try
                {
                    itemValue.Dispose();
                }
                catch (Exception ex)
                {
                    captureFailure(ex);
                }
            }
        }
        finally
        {
            RollbackCleanupReservation();
        }
    }

    // Emergency full sweep for an invalid cleanup call path that did not supply the required shared context. Normal
    // owner and public disposal always use reservation first.
    internal Exception? DetachAndDisposeWithoutReservation()
    {
        using SnapshotOperationLease operation = BeginOperation();
        return CleanupDetachedState(DetachState());
    }

    public void Dispose()
    {
        Uninitialize();
        MarkDirty();
    }

    private SnapshotOperationLease BeginOperation()
    {
        lock (_lifecycleGate)
        {
            int currentThreadId = Environment.CurrentManagedThreadId;
            if (_cleanupReserved && _cleanupOwnerThreadId != currentThreadId)
            {
                throw new InvalidOperationException(
                    "The graph snapshot cannot be read or updated while cleanup is reserved on another thread.");
            }

            if (_operationDepth != 0 && _operationOwnerThreadId != currentThreadId)
            {
                throw new InvalidOperationException(
                    "The graph snapshot cannot be read or updated concurrently on multiple threads.");
            }

            _operationOwnerThreadId = currentThreadId;
            _operationDepth++;
            return new SnapshotOperationLease(this);
        }
    }

    private void EndOperation()
    {
        lock (_lifecycleGate)
        {
            if (_operationDepth <= 0
                || _operationOwnerThreadId != Environment.CurrentManagedThreadId)
            {
                throw new InvalidOperationException("The graph snapshot operation is not owned by this thread.");
            }

            _operationDepth--;
            if (_operationDepth == 0)
            {
                _operationOwnerThreadId = 0;
                Monitor.PulseAll(_lifecycleGate);
            }
        }
    }

    private void BeginCleanupReservation(bool allowCurrentOperation)
    {
        lock (_lifecycleGate)
        {
            int currentThreadId = Environment.CurrentManagedThreadId;
            if (_cleanupReserved)
                throw new InvalidOperationException("Graph snapshot cleanup is already reserved.");

            if (_operationDepth != 0
                && (!allowCurrentOperation || _operationOwnerThreadId != currentThreadId))
            {
                throw new InvalidOperationException(
                    "The graph snapshot cannot be disposed while an update or read operation is in progress.");
            }

            _cleanupReserved = true;
            _cleanupOwnerThreadId = currentThreadId;
        }
    }

    private void ValidateCleanupReservation()
    {
        lock (_lifecycleGate)
        {
            if (!_cleanupReserved
                || _cleanupOwnerThreadId != Environment.CurrentManagedThreadId)
            {
                throw new InvalidOperationException("The graph snapshot resources were not reserved for this cleanup.");
            }
        }
    }

    private ref struct SnapshotOperationLease
    {
        private GraphSnapshot? _owner;

        public SnapshotOperationLease(GraphSnapshot owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            GraphSnapshot? owner = _owner;
            if (owner == null)
                return;

            _owner = null;
            owner.EndOperation();
        }
    }

    private sealed class CleanupCoordinator(GraphSnapshot snapshot, bool allowCurrentOperation) : EngineObject.Resource
    {
        private GeneratedResourceCleanupContext? _cleanupContext;

        protected override void PrepareGeneratedResourceCleanupCore(
            bool disposing,
            GeneratedResourceCleanupContext context)
        {
            if (disposing)
            {
                _cleanupContext = context;
                snapshot.BeginCleanupReservation(allowCurrentOperation);
                try
                {
                    foreach (GraphNode.Resource resource in snapshot._resources)
                    {
                        context.Reserve(resource);
                    }
                }
                catch
                {
                    snapshot.RollbackCleanupReservation();
                    throw;
                }
            }

            base.PrepareGeneratedResourceCleanupCore(disposing, context);
        }

        protected override void RollbackGeneratedResourceCleanupCore()
        {
            _cleanupContext = null;
            snapshot.RollbackCleanupReservation();
            base.RollbackGeneratedResourceCleanupCore();
        }

        protected override void Dispose(bool disposing)
        {
            Exception? failure = null;
            if (disposing)
            {
                GeneratedResourceCleanupContext? context = _cleanupContext;
                _cleanupContext = null;
                if (context == null)
                {
                    failure = new InvalidOperationException(
                        "The graph resources were not reserved before cleanup.");
                    snapshot.RollbackCleanupReservation();
                    _ = snapshot.DetachAndDisposeWithoutReservation();
                }
                else
                {
                    NodeGraphDisposal.Capture(
                        () => snapshot.DisposeAfterResourcesReserved(context.DisposeOwned, context.Capture),
                        ref failure);
                }
            }

            NodeGraphDisposal.Capture(() => base.Dispose(disposing), ref failure);
            NodeGraphDisposal.ThrowIfFailed(failure);
        }
    }

    private readonly record struct DetachedState(
        GraphNode.Resource[] Resources,
        IItemValue[] ItemValues,
        bool[] InitializationAttempted);
}

internal static class NodeGraphDisposal
{
    public static void Capture(IDisposable? disposable, ref Exception? failure)
    {
        if (disposable == null)
            return;

        Capture(disposable.Dispose, ref failure);
    }

    public static void Capture(Action cleanup, ref Exception? failure)
    {
        try
        {
            cleanup();
        }
        catch (Exception ex)
        {
            failure ??= ex;
        }
    }

    public static void ThrowIfFailed(Exception? failure)
    {
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
