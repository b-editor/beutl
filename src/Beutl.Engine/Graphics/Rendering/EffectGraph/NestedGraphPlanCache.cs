namespace Beutl.Graphics.Rendering;

/// <summary>
/// Hierarchical cache storage for nested graphs. Each persisted render node owns one root; node ordinals are scoped
/// below their parent branch, so identical local ordinals in separate branches never collide. The cache contains
/// compiled CPU plans only and follows the owning render node's lifetime. Like the root <see cref="PlanCache"/>,
/// it is render-thread-affine and intentionally lock-free; it must not be shared across concurrent render threads.
/// </summary>
internal sealed class NestedGraphPlanCache
{
    internal static readonly object NoGraphicsContext = new();

    private readonly Dictionary<int, NestedGraphNodePlanCache> _nodes = [];

    public NestedGraphNodePlanCache GetNode(int ordinal)
    {
        if (!_nodes.TryGetValue(ordinal, out NestedGraphNodePlanCache? node))
        {
            node = new NestedGraphNodePlanCache();
            _nodes.Add(ordinal, node);
        }

        return node;
    }

    public void PruneNodes(HashSet<int> visitedOrdinals)
    {
        foreach (int ordinal in _nodes.Keys.ToArray())
        {
            if (!visitedOrdinals.Contains(ordinal))
                _nodes.Remove(ordinal);
        }
    }
}

internal sealed class NestedGraphNodePlanCache
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
        foreach (int branchIndex in _branches.Keys.ToArray())
        {
            if (!liveBranchIndices.Contains(branchIndex))
                _branches.Remove(branchIndex);
        }
    }
}

internal sealed class NestedGraphBranchPlanCache
{
    public PlanCache Plan { get; } = new();

    public NestedGraphPlanCache Children { get; } = new();
}
