using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed class ExecutionIslandExecutionLedger
{
    private readonly ExecutionIslandPlan _plan;
    private readonly Dictionary<ExecutionIslandId, int> _expectedCompletionOrder;
    private readonly HashSet<ExecutionIslandId> _active = [];
    private readonly HashSet<ExecutionIslandId> _completed = [];
    private int _lastCompletedOrder = -1;

    public ExecutionIslandExecutionLedger(
        ExecutionIslandPlan plan,
        RecordedRenderGraph graph,
        ImmutableArray<RenderFragmentReference> roots,
        RenderCacheResolution cacheResolution)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(cacheResolution);
        if (roots.IsDefault)
            throw new ArgumentException("Publication roots must be initialized.", nameof(roots));

        var graphReferences = new Dictionary<RenderFragmentId, RenderFragmentReference>();
        foreach (RecordedRenderFragment recorded in graph.Fragments)
        {
            if (recorded.Payload is not RenderFragmentReference reference || reference.Id != recorded.Id)
            {
                throw new InvalidOperationException(
                    "The execution graph contains a fragment without its committed semantic reference.");
            }
            graphReferences.Add(recorded.Id, reference);
        }

        HashSet<RenderFragmentId> cacheHits = cacheResolution.CollectPrunedHitProducers();
        HashSet<RenderFragmentReference> reachable = GetReachableReferences(
            roots,
            graphReferences,
            cacheHits);
        foreach (ExecutionIsland island in plan.Islands)
        {
            foreach (RenderFragmentId fragmentId in island.Fragments)
            {
                if (!graphReferences.TryGetValue(fragmentId, out RenderFragmentReference? reference)
                    || !reachable.Contains(reference))
                {
                    throw new InvalidOperationException(
                        "An execution island contains a fragment that is not reachable from publication roots.");
                }
            }
        }

        foreach (RenderFragmentReference reference in reachable)
        {
            RenderFragmentId id = reference.Id!.Value;
            if (cacheHits.Contains(id)
                || reference.Kind is RenderFragmentKind.ContributeValues or RenderFragmentKind.MaterializedInput)
            {
                continue;
            }
            if (!plan.TryGetMembership(reference, out _))
            {
                throw new InvalidOperationException(
                    $"Executable fragment '{id.Value}' is not assigned to an execution island.");
            }
        }

        var expected = new List<ExecutionIsland>();
        var emitted = new HashSet<ExecutionIslandId>();
        var visited = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        var visiting = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        foreach (RenderFragmentReference root in roots)
            Visit(root, cacheHits, expected, emitted, visited, visiting);

        if (emitted.Count != plan.Islands.Length)
        {
            throw new InvalidOperationException(
                "Every planned execution island must be reachable in publication dependency order.");
        }
        _expectedCompletionOrder = expected
            .Select(static (island, index) => (island.Id, index))
            .ToDictionary(static item => item.Id, static item => item.index);
    }

    public ExecutionIsland Begin(RenderFragmentReference fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (!_plan.TryGetMembership(fragment, out ExecutionIslandMembership membership))
            throw new InvalidOperationException("The executable fragment is not assigned to an execution island.");
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
        if (!_expectedCompletionOrder.TryGetValue(island.Id, out int order))
            throw new InvalidOperationException("The completed execution island is not part of the request schedule.");
        if (order <= _lastCompletedOrder)
        {
            throw new InvalidOperationException(
                "Execution islands completed outside dependency and painter order.");
        }
        _lastCompletedOrder = order;
    }

    public void AbandonActive()
    {
        _active.Clear();
    }

    public void ValidateCompleted(
        bool allowSkippedIslands = false,
        IReadOnlySet<ExecutionIslandId>? regionEmptyIslands = null)
    {
        if (_active.Count != 0)
            throw new InvalidOperationException("An execution island was left active at request completion.");
        if (allowSkippedIslands)
            return;

        bool hasIncompleteIsland = false;
        foreach (ExecutionIslandId id in _expectedCompletionOrder.Keys)
        {
            if (!_completed.Contains(id)
                && (regionEmptyIslands is null || !regionEmptyIslands.Contains(id)))
            {
                hasIncompleteIsland = true;
                break;
            }
        }

        if (hasIncompleteIsland)
        {
            throw new InvalidOperationException(
                "Every scheduled execution island must complete before request publication.");
        }
    }

    private void Visit(
        RenderFragmentReference reference,
        IReadOnlySet<RenderFragmentId> cacheHits,
        ICollection<ExecutionIsland> expected,
        ISet<ExecutionIslandId> emitted,
        ISet<RenderFragmentReference> visited,
        ISet<RenderFragmentReference> visiting)
    {
        if (visiting.Contains(reference))
            throw new InvalidOperationException("The execution graph contains a dependency cycle.");
        if (!visited.Add(reference))
            return;
        visiting.Add(reference);
        try
        {
            RenderFragmentId id = reference.Id
                ?? throw new InvalidOperationException("An execution fragment is not committed.");
            if (cacheHits.Contains(id))
                return;

            if (_plan.TryGetMembership(reference, out ExecutionIslandMembership membership))
            {
                if (membership.ShaderRun is { } run)
                {
                    if (!membership.IsTerminal)
                    {
                        throw new InvalidOperationException(
                            "A non-terminal Shader stage cannot be scheduled as an independent entry point.");
                    }
                    Visit(run.Input, cacheHits, expected, emitted, visited, visiting);
                }
                else
                {
                    foreach (RenderFragmentReference input in EnumerateExecutionInputs(reference))
                        Visit(input, cacheHits, expected, emitted, visited, visiting);
                }

                if (emitted.Add(membership.Island.Id))
                    expected.Add(membership.Island);
                return;
            }

            foreach (RenderFragmentReference input in EnumerateExecutionInputs(reference))
                Visit(input, cacheHits, expected, emitted, visited, visiting);
        }
        finally
        {
            visiting.Remove(reference);
        }
    }

    private static IEnumerable<RenderFragmentReference> EnumerateExecutionInputs(
        RenderFragmentReference reference)
    {
        ImmutableArray<RenderFragmentReference> inputs = reference.ExecutionInputs;
        if (reference.Kind == RenderFragmentKind.OpacityMask && inputs.Length > 1)
        {
            for (int index = 1; index < inputs.Length; index++)
                yield return inputs[index];
            yield return inputs[0];
            yield break;
        }

        foreach (RenderFragmentReference input in inputs)
            yield return input;
    }

    private static HashSet<RenderFragmentReference> GetReachableReferences(
        ImmutableArray<RenderFragmentReference> roots,
        IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> graphReferences,
        IReadOnlySet<RenderFragmentId> cacheHits)
    {
        var result = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        // Pushing in reverse leaves the roots on top in their declared order, which is what Reverse() was
        // for; going through LINQ boxes the ImmutableArray and buffers a copy on every frame.
        var pending = new Stack<RenderFragmentReference>(roots.Length);
        for (int index = roots.Length - 1; index >= 0; index--)
            pending.Push(roots[index]);
        while (pending.TryPop(out RenderFragmentReference? reference))
        {
            RenderFragmentId id = reference.Id
                ?? throw new InvalidOperationException("A publication root is not committed.");
            if (!graphReferences.TryGetValue(id, out RenderFragmentReference? graphReference)
                || !ReferenceEquals(reference, graphReference))
            {
                throw new ArgumentException("A publication root is not part of the recorded graph.", nameof(roots));
            }
            if (!result.Add(reference) || cacheHits.Contains(id))
                continue;
            ImmutableArray<RenderFragmentReference> inputs = reference.ExecutionInputs;
            for (int index = inputs.Length - 1; index >= 0; index--)
                pending.Push(inputs[index]);
        }
        return result;
    }
}
