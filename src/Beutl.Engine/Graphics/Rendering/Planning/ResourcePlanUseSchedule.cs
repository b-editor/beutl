using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Beutl.Graphics.Rendering;

internal readonly record struct ResourcePlanFragmentLifetime(
    RenderFragmentReference Fragment,
    int AcquisitionPosition,
    ImmutableArray<int> ConsumerPositions)
{
    public int LastUsePosition
        => ConsumerPositions.IsDefaultOrEmpty
            ? AcquisitionPosition
            : ConsumerPositions[^1];
}

/// <summary>
/// Structural resource-use schedule for a recorded request. Runtime-discovered streams share their producer
/// interval; their exact target sizes remain selected by the pool when the callback publishes each value.
/// </summary>
internal sealed class ResourcePlanUseSchedule
{
    private ResourcePlanUseSchedule(ImmutableArray<ResourcePlanFragmentLifetime> lifetimes)
    {
        Lifetimes = lifetimes;
    }

    public ImmutableArray<ResourcePlanFragmentLifetime> Lifetimes { get; }

    public ResourcePlanUseTracker BeginExecution()
        => new(Lifetimes);

    internal static ResourcePlanUseSchedule Create(
        IReadOnlyList<RenderFragmentReference> roots,
        IReadOnlySet<RenderFragmentId>? terminalFragmentIds = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        terminalFragmentIds ??= new HashSet<RenderFragmentId>();
        var ordered = new List<RenderFragmentReference>();
        var visiting = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        foreach (RenderFragmentReference root in roots)
        {
            ArgumentNullException.ThrowIfNull(root);
            Visit(root, terminalFragmentIds, visiting, visited, ordered);
        }

        var consumers = new Dictionary<RenderFragmentReference, List<int>>(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < ordered.Count; index++)
        {
            consumers.Add(ordered[index], []);
        }

        for (int index = 0; index < ordered.Count; index++)
        {
            RenderFragmentReference fragment = ordered[index];
            if (fragment.Id is { } id && terminalFragmentIds.Contains(id))
                continue;
            foreach (RenderFragmentReference input in fragment.ExecutionInputs)
                consumers[input].Add(index);
        }

        for (int index = 0; index < roots.Count; index++)
            consumers[roots[index]].Add(checked(ordered.Count + index));

        var lifetimes = new ResourcePlanFragmentLifetime[ordered.Count];
        for (int index = 0; index < lifetimes.Length; index++)
        {
            RenderFragmentReference fragment = ordered[index];
            // LastUsePosition reads the final element, so the positions have to arrive sorted.
            int[] consumerPositions = [.. consumers[fragment]];
            Array.Sort(consumerPositions);
            lifetimes[index] = new ResourcePlanFragmentLifetime(
                fragment,
                index,
                ImmutableCollectionsMarshal.AsImmutableArray(consumerPositions));
        }

        return new ResourcePlanUseSchedule(ImmutableCollectionsMarshal.AsImmutableArray(lifetimes));

        static void Visit(
            RenderFragmentReference fragment,
            IReadOnlySet<RenderFragmentId> terminalFragmentIds,
            HashSet<RenderFragmentReference> visiting,
            HashSet<RenderFragmentReference> visited,
            List<RenderFragmentReference> ordered)
        {
            if (visited.Contains(fragment))
                return;
            if (!visiting.Add(fragment))
                throw new InvalidOperationException("The resource-use graph contains a fragment cycle.");

            if (fragment.Id is not { } id || !terminalFragmentIds.Contains(id))
            {
                foreach (RenderFragmentReference input in fragment.ExecutionInputs)
                    Visit(input, terminalFragmentIds, visiting, visited, ordered);
            }

            visiting.Remove(fragment);
            visited.Add(fragment);
            ordered.Add(fragment);
        }
    }
}

internal sealed class ResourcePlanUseTracker
{
    private readonly Dictionary<RenderFragmentReference, int> _remainingUses;

    internal ResourcePlanUseTracker(ImmutableArray<ResourcePlanFragmentLifetime> lifetimes)
    {
        _remainingUses = new Dictionary<RenderFragmentReference, int>(
            lifetimes.Length,
            ReferenceEqualityComparer.Instance);
        foreach (ResourcePlanFragmentLifetime lifetime in lifetimes)
            _remainingUses.Add(lifetime.Fragment, lifetime.ConsumerPositions.Length);
    }

    /// <summary>Completes one authored edge/root use and returns true at the producer's last use.</summary>
    public bool CompleteUse(RenderFragmentReference fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (!_remainingUses.TryGetValue(fragment, out int remaining) || remaining <= 0)
        {
            throw new InvalidOperationException(
                "A render fragment was consumed more times than its resource plan declares.");
        }

        remaining--;
        _remainingUses[fragment] = remaining;
        return remaining == 0;
    }

    public int GetRemainingUseCount(RenderFragmentReference fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        return _remainingUses.TryGetValue(fragment, out int remaining)
            ? remaining
            : throw new InvalidOperationException(
                "A render fragment is not part of the resource-use schedule.");
    }
}
