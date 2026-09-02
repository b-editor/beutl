using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering;

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
