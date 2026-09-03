namespace Beutl.Graphics.Rendering.Cache;

internal sealed class RenderNodeCacheLifecycle
{
    private readonly NodeSnapshot[] _nodes;
    private bool _completed;

    private RenderNodeCacheLifecycle(NodeSnapshot[] nodes)
    {
        _nodes = nodes;
    }

    internal static RenderNodeCacheLifecycle Create(RenderNode root)
    {
        var snapshots = new Dictionary<RenderNode, NodeSnapshot>(ReferenceEqualityComparer.Instance);
        Collect(root, snapshots);

        var ancestors = new HashSet<NodeSnapshot>();
        foreach (NodeSnapshot snapshot in snapshots.Values)
        {
            // A reported change is all this has to go on, and HasChanges is one flag per node: whichever
            // root completes first clears it for the others. A node reached from two roots therefore has
            // its change seen by one of them, and the others keep serving what they already cached.
            if (!snapshot.WasDirty)
                continue;

            MarkNodeAndAncestors(snapshot, ancestors);
        }

        foreach (NodeSnapshot snapshot in snapshots.Values)
        {
            if (snapshot.IsInvalidated && !snapshot.Node.IsDisposed)
            {
                snapshot.Node.Cache.Reset();
            }
        }

        return new RenderNodeCacheLifecycle([.. snapshots.Values]);
    }

    internal void CompleteSuccessfully(bool advanceWarmup)
    {
        if (_completed)
            throw new InvalidOperationException("A render-node cache lifecycle can complete only once.");

        var changedDuringRequest = new List<NodeSnapshot>();
        foreach (NodeSnapshot snapshot in _nodes)
        {
            if (snapshot.Node.HasChanges
                && (!snapshot.WasDirty || snapshot.Node.ChangeVersion != snapshot.ObservedChangeVersion))
            {
                changedDuringRequest.Add(snapshot);
            }
        }

        if (changedDuringRequest.Count != 0)
        {
            var ancestors = new HashSet<NodeSnapshot>();
            foreach (NodeSnapshot snapshot in changedDuringRequest)
            {
                MarkNodeAndAncestors(snapshot, ancestors);
            }

            foreach (NodeSnapshot snapshot in ancestors)
            {
                if (!snapshot.Node.IsDisposed)
                {
                    snapshot.Node.Cache.Reset();
                }
            }
        }

        foreach (NodeSnapshot snapshot in _nodes)
        {
            if (snapshot.WasDirty)
            {
                snapshot.Node.ClearChanges(snapshot.ObservedChangeVersion);
            }
        }

        if (advanceWarmup)
        {
            foreach (NodeSnapshot snapshot in _nodes)
            {
                if (!snapshot.IsInvalidated
                    && !snapshot.Node.IsDisposed
                    && !snapshot.Node.HasChanges
                    && snapshot.Node.ChangeVersion == snapshot.ObservedChangeVersion)
                {
                    snapshot.Node.Cache.RecordSuccessfulStableRequest();
                }
            }
        }

        _completed = true;
    }

    private static NodeSnapshot Collect(
        RenderNode node,
        IDictionary<RenderNode, NodeSnapshot> snapshots)
    {
        if (snapshots.TryGetValue(node, out NodeSnapshot? existing))
        {
            if (existing.IsVisiting)
            {
                throw new InvalidOperationException(
                    "A render-node ChildNodes cycle was detected while preparing cache lifecycle state.");
            }

            return existing;
        }

        var snapshot = new NodeSnapshot(node, node.HasChanges, node.ChangeVersion)
        {
            IsVisiting = true,
        };
        snapshots.Add(node, snapshot);
        try
        {
            ReadOnlySpan<RenderNode> children = node.ChildNodes;
            for (int i = 0; i < children.Length; i++)
            {
                RenderNode child = children[i];
                ArgumentNullException.ThrowIfNull(child);
                NodeSnapshot childSnapshot = Collect(child, snapshots);
                childSnapshot.Parents.Add(snapshot);
            }
        }
        finally
        {
            snapshot.IsVisiting = false;
        }

        return snapshot;
    }

    private static void MarkNodeAndAncestors(NodeSnapshot node, ISet<NodeSnapshot> visited)
    {
        if (!visited.Add(node))
            return;

        node.IsInvalidated = true;
        foreach (NodeSnapshot parent in node.Parents)
        {
            MarkNodeAndAncestors(parent, visited);
        }
    }

    private sealed class NodeSnapshot(
        RenderNode node,
        bool wasDirty,
        long observedChangeVersion)
    {
        public RenderNode Node { get; } = node;

        public bool WasDirty { get; } = wasDirty;

        public long ObservedChangeVersion { get; } = observedChangeVersion;

        public List<NodeSnapshot> Parents { get; } = [];

        public bool IsVisiting { get; set; }

        public bool IsInvalidated { get; set; }
    }
}
