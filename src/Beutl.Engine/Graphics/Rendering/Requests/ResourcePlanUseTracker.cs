namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// Tracks remaining graph-edge and root uses for each fragment. Runtime-discovered value streams share their
/// producer fragment's count.
/// </summary>
internal sealed class ResourcePlanUseTracker
{
    private readonly Dictionary<RenderFragmentReference, int> _remainingUses;

    private ResourcePlanUseTracker(Dictionary<RenderFragmentReference, int> remainingUses)
    {
        _remainingUses = remainingUses;
    }

    internal static ResourcePlanUseTracker Create(
        IReadOnlyList<RenderFragmentReference> roots,
        IReadOnlySet<RenderFragmentId>? terminalFragmentIds = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var remainingUses = new Dictionary<RenderFragmentReference, int>(
            roots.Count,
            ReferenceEqualityComparer.Instance);
        var visiting = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        foreach (RenderFragmentReference root in roots)
        {
            ArgumentNullException.ThrowIfNull(root);
            Visit(root, terminalFragmentIds, visiting, remainingUses);
            remainingUses[root]++;
        }

        return new ResourcePlanUseTracker(remainingUses);

        static void Visit(
            RenderFragmentReference fragment,
            IReadOnlySet<RenderFragmentId>? terminalFragmentIds,
            HashSet<RenderFragmentReference> visiting,
            Dictionary<RenderFragmentReference, int> remainingUses)
        {
            if (remainingUses.ContainsKey(fragment))
                return;
            if (!visiting.Add(fragment))
                throw new InvalidOperationException("The resource-use graph contains a fragment cycle.");

            if (fragment.Id is not { } id || terminalFragmentIds?.Contains(id) != true)
            {
                foreach (RenderFragmentReference input in fragment.ExecutionInputs)
                {
                    Visit(input, terminalFragmentIds, visiting, remainingUses);
                    remainingUses[input]++;
                }
            }

            visiting.Remove(fragment);
            remainingUses.Add(fragment, 0);
        }
    }

    /// <summary>Completes one authored edge/root use and returns true at the producer's last use.</summary>
    public bool CompleteUse(RenderFragmentReference fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (!_remainingUses.TryGetValue(fragment, out int remaining) || remaining <= 0)
        {
            throw new InvalidOperationException(
                "A render fragment was consumed more times than its planned use count declares.");
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
                "A render fragment is not part of the resource-use tracker.");
    }
}
