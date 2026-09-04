namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// Guards the runtime execution of a planned island set: an island executes at most once, only an active
/// island completes, and every planned island has completed by publication.
/// </summary>
internal sealed class ExecutionIslandExecutionLedger
{
    private const byte Active = 1;
    private const byte Completed = 2;

    private readonly ExecutionIslandPlan _plan;
    private readonly RecordedRenderGraph _graph;
    private readonly byte[] _states;
    private int _activeCount;
    private int _completedCount;

    public ExecutionIslandExecutionLedger(ExecutionIslandPlan plan, RecordedRenderGraph graph)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        if (graph.Fragments.Length != plan.FragmentCount)
        {
            throw new ArgumentException(
                "The execution graph does not match the immutable plan size.",
                nameof(graph));
        }
        _states = new byte[plan.Islands.Length];
    }

    public ExecutionIsland Begin(RenderFragmentReference fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (!_plan.TryGetMembership(_graph, fragment, out ExecutionIslandMembership membership))
            throw new InvalidOperationException("The executable fragment is not assigned to an execution island.");
        return Begin(membership);
    }

    public ExecutionIsland Begin(ExecutionIslandMembership membership)
    {
        if (membership.Island.ShaderRun is not null && !membership.IsTerminal)
        {
            throw new InvalidOperationException(
                "A non-terminal Shader stage cannot execute independently of its compiled island.");
        }

        ExecutionIsland island = membership.Island;
        if (_states[island.Index] != 0)
            throw new InvalidOperationException("An execution island cannot execute more than once.");
        _states[island.Index] = Active;
        _activeCount++;
        return island;
    }

    public void Complete(ExecutionIsland island)
    {
        if (_states[island.Index] != Active)
            throw new InvalidOperationException("Only an active execution island can complete.");
        _states[island.Index] = Completed;
        _activeCount--;
        _completedCount++;
    }

    public void AbandonActive()
    {
        if (_activeCount == 0)
            return;
        for (int index = 0; index < _states.Length; index++)
        {
            if (_states[index] == Active)
                _states[index] = 0;
        }
        _activeCount = 0;
    }

    public void ValidateCompleted(bool allowSkippedIslands = false, RegionAnalysis? regions = null)
    {
        if (_activeCount != 0)
            throw new InvalidOperationException("An execution island was left active at request completion.");
        if (allowSkippedIslands)
            return;
        if (_completedCount == _states.Length)
            return;

        foreach (ExecutionIsland island in _plan.Islands)
        {
            if (_states[island.Index] == Completed)
                continue;
            // An island whose every fragment resolved to an empty region is legitimately never entered.
            // No ordinary frame has one, so the probe stays on this failure path.
            if (regions is not null && IsRegionEmpty(island, regions))
                continue;

            throw new InvalidOperationException(
                "Every scheduled execution island must complete before request publication.");
        }
    }

    private bool IsRegionEmpty(ExecutionIsland island, RegionAnalysis regions)
    {
        foreach (int fragmentIndex in island.FragmentIndices)
        {
            RenderFragmentId fragmentId = _graph.Fragments[fragmentIndex].Id
                ?? throw new InvalidOperationException("An execution-plan fragment is not committed.");
            if (!regions.FragmentRequirements.TryGetValue(fragmentId, out RequiredRegion requirement)
                || !requirement.IsEmpty)
            {
                return false;
            }

            if (regions.TargetAccessRequirements.TryGetValue(
                    fragmentId,
                    out RequiredRegion targetRequirement)
                && !targetRequirement.IsEmpty)
            {
                return false;
            }
        }

        return true;
    }
}
