namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// Structural resource-use schedule for a recorded request: how many times each fragment is consumed, by an
/// execution edge or by an authored root. Runtime-discovered streams share their producer interval; their
/// exact target sizes remain selected by the pool when the callback publishes each value.
/// </summary>
internal sealed class ResourcePlanUseSchedule
{
    private readonly Dictionary<RenderFragmentReference, int> _useCounts;

    private ResourcePlanUseSchedule(Dictionary<RenderFragmentReference, int> useCounts)
    {
        _useCounts = useCounts;
    }

    public IReadOnlyDictionary<RenderFragmentReference, int> UseCounts => _useCounts;

    public ResourcePlanUseTracker BeginExecution()
        => new(_useCounts);

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

        var useCounts = new Dictionary<RenderFragmentReference, int>(
            ordered.Count,
            ReferenceEqualityComparer.Instance);
        foreach (RenderFragmentReference fragment in ordered)
            useCounts.Add(fragment, 0);

        foreach (RenderFragmentReference fragment in ordered)
        {
            if (fragment.Id is { } id && terminalFragmentIds.Contains(id))
                continue;
            foreach (RenderFragmentReference input in fragment.ExecutionInputs)
                useCounts[input]++;
        }

        for (int index = 0; index < roots.Count; index++)
            useCounts[roots[index]]++;

        return new ResourcePlanUseSchedule(useCounts);

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
