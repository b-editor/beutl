using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>The graph-independent, immutable execution topology for one structural request shape.</summary>
internal sealed class ExecutionIslandPlan
{
    private readonly int[] _islandByFragment;

    public ExecutionIslandPlan(
        int fragmentCount,
        ImmutableArray<ExecutionIsland> islands)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fragmentCount);
        if (islands.IsDefault)
            throw new ArgumentException("Execution islands must be initialized.", nameof(islands));

        FragmentCount = fragmentCount;
        Islands = islands;
        _islandByFragment = new int[fragmentCount];
        for (int index = 0; index < islands.Length; index++)
        {
            ExecutionIsland island = islands[index];
            if (island.Index != index)
            {
                throw new ArgumentException(
                    "Execution-island indices must match their immutable plan order.",
                    nameof(islands));
            }

            for (int fragment = 0; fragment < island.FragmentIndices.Length; fragment++)
            {
                int fragmentIndex = island.FragmentIndices[fragment];
                if ((uint)fragmentIndex >= (uint)fragmentCount)
                {
                    throw new ArgumentException(
                        "An execution island contains a fragment outside the recorded graph.",
                        nameof(islands));
                }
                if (_islandByFragment[fragmentIndex] != 0)
                {
                    throw new ArgumentException(
                        "A fragment cannot belong to more than one execution island.",
                        nameof(islands));
                }
                _islandByFragment[fragmentIndex] = island.Index + 1;
            }
        }
    }

    public int FragmentCount { get; }

    public ImmutableArray<ExecutionIsland> Islands { get; }

    public bool TryGetMembership(
        RecordedRenderGraph graph,
        RenderFragmentReference fragment,
        out ExecutionIslandMembership membership)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(fragment);
        if (graph.Fragments.Length != FragmentCount)
        {
            throw new InvalidOperationException(
                "An execution plan cannot be used with a graph of a different size.");
        }
        if (fragment.Id is not { } id)
            throw new InvalidOperationException("An execution-plan fragment is not committed.");
        if (id.RequestId != graph.RequestId
            || id.Value <= 0
            || id.Value > graph.Fragments.Length
            || !ReferenceEquals(graph.Fragments[checked((int)id.Value - 1)], fragment))
        {
            membership = default;
            return false;
        }

        int fragmentIndex = checked((int)id.Value - 1);
        int islandNumber = _islandByFragment[fragmentIndex];
        if (islandNumber == 0)
        {
            membership = default;
            return false;
        }

        ExecutionIsland island = Islands[islandNumber - 1];
        membership = new ExecutionIslandMembership(
            island,
            fragmentIndex == island.FragmentIndices[^1]);
        return true;
    }

    public ExecutionIslandExecutionLedger CreateExecutionLedger(RecordedRenderGraph graph) => new(this, graph);
}
