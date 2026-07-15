namespace Beutl.Graphics.Rendering;

/// <summary>
/// Hierarchical cache storage for nested graphs. Each persisted render node owns one root; node ordinals are scoped
/// below their parent branch, so identical local ordinals in separate branches never collide. Each scope contains
/// CPU-only compiled plans plus persistent custom render-node instances; only the latter own runtime state. The cache
/// follows the owning render node's lifetime. Like the root <see cref="PlanCache"/>, hierarchy mutation is
/// render-thread-affine and must not be shared across concurrent render threads; only output-lease retirement is
/// synchronized because operation disposal can outlive the render call.
/// </summary>
internal sealed class NestedGraphPlanCache : IDisposable
{
    internal static readonly object NoGraphicsContext = new();

    private readonly Dictionary<int, NestedGraphNodePlanCache> _nodes = [];
    private readonly Dictionary<int, CustomRenderNodePlanCache> _customNodes = [];
    private bool _disposed;

    public NestedGraphNodePlanCache GetNode(int ordinal)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_nodes.TryGetValue(ordinal, out NestedGraphNodePlanCache? node))
        {
            node = new NestedGraphNodePlanCache();
            _nodes.Add(ordinal, node);
        }

        return node;
    }

    public CustomRenderNodePlanCache GetCustomNode(int ordinal)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_customNodes.TryGetValue(ordinal, out CustomRenderNodePlanCache? node))
        {
            node = new CustomRenderNodePlanCache();
            _customNodes.Add(ordinal, node);
        }

        return node;
    }

    public void PruneNodes(HashSet<int> visitedOrdinals)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Exception? cleanupFailure = null;
        foreach (int ordinal in _nodes.Keys.ToArray())
        {
            if (!visitedOrdinals.Contains(ordinal))
            {
                NestedGraphNodePlanCache node = _nodes[ordinal];
                _nodes.Remove(ordinal);
                CaptureDisposeFailure(node, ref cleanupFailure);
            }
        }

        if (cleanupFailure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    public void PruneCustomNodes(HashSet<int> visitedOrdinals)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Exception? cleanupFailure = null;
        foreach (int ordinal in _customNodes.Keys.ToArray())
        {
            if (!visitedOrdinals.Contains(ordinal))
            {
                CustomRenderNodePlanCache node = _customNodes[ordinal];
                _customNodes.Remove(ordinal);
                CaptureDisposeFailure(node, ref cleanupFailure);
            }
        }

        if (cleanupFailure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Exception? cleanupFailure = null;
        foreach (NestedGraphNodePlanCache node in _nodes.Values)
            CaptureDisposeFailure(node, ref cleanupFailure);
        foreach (CustomRenderNodePlanCache node in _customNodes.Values)
            CaptureDisposeFailure(node, ref cleanupFailure);
        _nodes.Clear();
        _customNodes.Clear();

        if (cleanupFailure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    public void NotifyServedFromCache()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Exception? cleanupFailure = null;
        foreach (NestedGraphNodePlanCache node in _nodes.Values)
        {
            try
            {
                node.NotifyServedFromCache();
            }
            catch (Exception ex)
            {
                cleanupFailure ??= ex;
            }
        }

        foreach (CustomRenderNodePlanCache node in _customNodes.Values)
        {
            try
            {
                node.NotifyServedFromCache();
            }
            catch (Exception ex)
            {
                cleanupFailure ??= ex;
            }
        }

        if (cleanupFailure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    private static void CaptureDisposeFailure(IDisposable disposable, ref Exception? cleanupFailure)
    {
        try
        {
            disposable.Dispose();
        }
        catch (Exception ex)
        {
            cleanupFailure ??= ex;
        }
    }
}

internal sealed class NestedGraphNodePlanCache : IDisposable
{
    private readonly Dictionary<int, NestedGraphBranchPlanCache> _branches = [];

    public NestedGraphBranchPlanCache GetBranch(int branchIndex)
    {
        if (!_branches.TryGetValue(branchIndex, out NestedGraphBranchPlanCache? branch))
        {
            branch = new NestedGraphBranchPlanCache();
            _branches.Add(branchIndex, branch);
        }

        return branch;
    }

    public void PruneBranches(IReadOnlySet<int> liveBranchIndices)
    {
        ArgumentNullException.ThrowIfNull(liveBranchIndices);
        Exception? cleanupFailure = null;
        foreach (int branchIndex in _branches.Keys.ToArray())
        {
            if (!liveBranchIndices.Contains(branchIndex))
            {
                NestedGraphBranchPlanCache branch = _branches[branchIndex];
                _branches.Remove(branchIndex);
                try
                {
                    branch.Dispose();
                }
                catch (Exception ex)
                {
                    cleanupFailure ??= ex;
                }
            }
        }

        if (cleanupFailure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    public void Dispose()
    {
        Exception? cleanupFailure = null;
        foreach (NestedGraphBranchPlanCache branch in _branches.Values)
        {
            try
            {
                branch.Dispose();
            }
            catch (Exception ex)
            {
                cleanupFailure ??= ex;
            }
        }

        _branches.Clear();
        if (cleanupFailure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    public void NotifyServedFromCache()
    {
        Exception? cleanupFailure = null;
        foreach (NestedGraphBranchPlanCache branch in _branches.Values)
        {
            try
            {
                branch.NotifyServedFromCache();
            }
            catch (Exception ex)
            {
                cleanupFailure ??= ex;
            }
        }

        if (cleanupFailure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }
}

internal sealed class NestedGraphBranchPlanCache : IDisposable
{
    public PlanCache Plan { get; } = new();

    public NestedGraphPlanCache Children { get; } = new();

    public void Dispose()
    {
        Plan.Invalidate();
        Children.Dispose();
    }

    public void NotifyServedFromCache() => Children.NotifyServedFromCache();
}

/// <summary>
/// Owns the persistent render-node instance for one custom descriptor ordinal. The cache is scoped by the same root,
/// nested-node, and branch hierarchy as compiled child plans, so equal local ordinals in separate branches cannot
/// share mutable node state.
/// </summary>
internal sealed class CustomRenderNodePlanCache : IDisposable
{
    private NodeEntry? _entry;
    private FilterEffectRenderNodeFactory? _factory;
    private long _resourceStructuralId;
    private bool _disposed;

    public NodeEntry GetOrCreate(
        Effects.FilterEffect.Resource resource,
        FilterEffectRenderNodeFactory factory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long structuralId = resource.StructuralId;
        if (_entry != null
            && ReferenceEquals(_factory, factory)
            && _resourceStructuralId == structuralId)
        {
            return _entry;
        }

        NodeEntry? previous = _entry;
        _entry = null;
        _factory = null;
        _resourceStructuralId = 0;
        previous?.Retire();

        FilterEffectRenderNode created = factory.Create(resource);
        var entry = new NodeEntry(created);
        _entry = entry;
        _factory = factory;
        _resourceStructuralId = structuralId;
        return entry;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        NodeEntry? entry = _entry;
        _entry = null;
        _factory = null;
        _resourceStructuralId = 0;
        entry?.Retire();
    }

    public void NotifyServedFromCache()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _entry?.Node.OnServedFromCache();
    }

    internal sealed class NodeEntry(FilterEffectRenderNode node)
    {
        private readonly object _gate = new();
        private FilterEffectRenderNode? _node = node;
        private int _activeOutputs;
        private bool _retired;

        public FilterEffectRenderNode Node
        {
            get
            {
                lock (_gate)
                    return _node ?? throw new ObjectDisposedException(nameof(NodeEntry));
            }
        }

        public Action AcquireOutputLease()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_node is null || _retired, this);
                _activeOutputs++;
            }

            int released = 0;
            return () =>
            {
                if (Interlocked.Exchange(ref released, 1) != 0)
                    return;

                FilterEffectRenderNode? dispose = null;
                lock (_gate)
                {
                    _activeOutputs--;
                    if (_retired && _activeOutputs == 0)
                    {
                        dispose = _node;
                        _node = null;
                    }
                }

                dispose?.Dispose();
            };
        }

        public void Retire()
        {
            FilterEffectRenderNode? dispose = null;
            lock (_gate)
            {
                if (_retired)
                    return;

                _retired = true;
                if (_activeOutputs == 0)
                {
                    dispose = _node;
                    _node = null;
                }
            }

            dispose?.Dispose();
        }
    }
}
