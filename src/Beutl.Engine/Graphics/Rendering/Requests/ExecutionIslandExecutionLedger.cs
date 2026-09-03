namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// Guards the runtime execution of a planned island set: an island executes at most once, only an active
/// island completes, and every planned island has completed by publication.
/// </summary>
internal sealed class ExecutionIslandExecutionLedger
{
    private readonly ExecutionIslandPlan _plan;
    private readonly HashSet<ExecutionIslandId> _active = [];
    private readonly HashSet<ExecutionIslandId> _completed = [];

    public ExecutionIslandExecutionLedger(ExecutionIslandPlan plan)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
    }

    public ExecutionIsland Begin(RenderFragmentReference fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (!_plan.TryGetMembership(fragment, out ExecutionIslandMembership membership))
            throw new InvalidOperationException("The executable fragment is not assigned to an execution island.");
        return Begin(membership);
    }

    public ExecutionIsland Begin(ExecutionIslandMembership membership)
    {
        if (membership.ShaderRun is not null && !membership.IsTerminal)
        {
            throw new InvalidOperationException(
                "A non-terminal Shader stage cannot execute independently of its compiled island.");
        }

        ExecutionIsland island = membership.Island;
        if (_completed.Contains(island.Id) || !_active.Add(island.Id))
            throw new InvalidOperationException("An execution island cannot execute more than once.");
        return island;
    }

    public void Complete(ExecutionIsland island)
    {
        ArgumentNullException.ThrowIfNull(island);
        if (!_active.Remove(island.Id))
            throw new InvalidOperationException("Only an active execution island can complete.");
        if (!_completed.Add(island.Id))
            throw new InvalidOperationException("An execution island cannot complete more than once.");
    }

    public void AbandonActive()
    {
        _active.Clear();
    }

    public void ValidateCompleted(bool allowSkippedIslands = false, RegionAnalysis? regions = null)
    {
        if (_active.Count != 0)
            throw new InvalidOperationException("An execution island was left active at request completion.");
        if (allowSkippedIslands)
            return;

        foreach (ExecutionIsland island in _plan.Islands)
        {
            if (_completed.Contains(island.Id))
                continue;
            // An island whose every fragment resolved to an empty region is legitimately never entered.
            // No ordinary frame has one, so the probe stays on this failure path.
            if (regions is not null && IsRegionEmpty(island, regions))
                continue;

            throw new InvalidOperationException(
                "Every scheduled execution island must complete before request publication.");
        }
    }

    private static bool IsRegionEmpty(ExecutionIsland island, RegionAnalysis regions)
    {
        foreach (RenderFragmentId fragmentId in island.Fragments)
        {
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
