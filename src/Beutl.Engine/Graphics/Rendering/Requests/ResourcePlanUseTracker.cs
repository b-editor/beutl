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
        RenderCacheResolution cacheResolution)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var remainingUses = new Dictionary<RenderFragmentReference, int>(
            roots.Count,
            ReferenceEqualityComparer.Instance);
        foreach (RenderFragmentReference root in roots)
        {
            ArgumentNullException.ThrowIfNull(root);
            Visit(root, cacheResolution, remainingUses);
            remainingUses[root]++;
        }

        return new ResourcePlanUseTracker(remainingUses);

        static void Visit(
            RenderFragmentReference fragment,
            RenderCacheResolution cacheResolution,
            Dictionary<RenderFragmentReference, int> remainingUses)
        {
            if (remainingUses.ContainsKey(fragment))
                return;

            if (fragment.Id is not { } id || !cacheResolution.HasHitProducer(id))
            {
                foreach (RenderFragmentReference input in fragment.ExecutionInputs)
                {
                    Visit(input, cacheResolution, remainingUses);
                    remainingUses[input]++;
                }
            }

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
