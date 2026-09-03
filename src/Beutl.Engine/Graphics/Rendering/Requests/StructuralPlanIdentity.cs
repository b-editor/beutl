using System.Collections.Immutable;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Rendering.Requests;

/// <summary>
/// Complete parameter-independent identity for one recorded request graph. Nested requests use their own
/// structural-plan cache slots and identities.
/// </summary>
internal sealed class StructuralPlanIdentity : IEquatable<StructuralPlanIdentity>
{
    private readonly RenderRequestPlanIdentity _request;
    private readonly SkslBackendBudget _shaderBudget;
    private readonly StructuralFragmentIdentity[] _fragments;
    private readonly int[] _publicationRoots;
    private readonly StructuralCacheBoundaryIdentity[] _cacheBoundaries;

    private StructuralPlanIdentity(
        RenderRequestPlanIdentity request,
        SkslBackendBudget shaderBudget,
        StructuralFragmentIdentity[] fragments,
        int[] publicationRoots,
        StructuralCacheBoundaryIdentity[] cacheBoundaries)
    {
        _request = request;
        _shaderBudget = shaderBudget;
        _fragments = fragments;
        _publicationRoots = publicationRoots;
        _cacheBoundaries = cacheBoundaries;
    }

    public static StructuralPlanIdentity Create(
        RenderRequestPlanIdentity request,
        RecordedRenderGraph graph,
        SkslBackendBudget shaderBudget,
        RenderCacheResolution? cacheResolution = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(shaderBudget);

        RenderFragmentReference[] references = new RenderFragmentReference[graph.Fragments.Length];
        var indexes = new Dictionary<RenderFragmentReference, int>(
            graph.Fragments.Length,
            ReferenceEqualityComparer.Instance);
        for (int index = 0; index < graph.Fragments.Length; index++)
        {
            RecordedRenderFragment recorded = graph.Fragments[index];
            if (recorded.Id.RequestId != graph.RequestId || recorded.Id.Value != index + 1L)
                throw new InvalidOperationException("A recorded fragment has a non-canonical graph ID.");
            if (recorded.Payload is not RenderFragmentReference reference || reference.Id != recorded.Id)
            {
                throw new InvalidOperationException(
                    "A recorded fragment is missing its canonical semantic reference.");
            }

            references[index] = reference;
            indexes.Add(reference, index);
        }

        var fragments = new StructuralFragmentIdentity[references.Length];
        for (int index = 0; index < references.Length; index++)
            fragments[index] = StructuralFragmentIdentity.Create(references[index], indexes);

        ImmutableArray<RenderFragmentId> roots = graph.PublicationRoots;
        int[] publicationRoots = roots.Length == 0 ? [] : new int[roots.Length];
        for (int index = 0; index < roots.Length; index++)
            publicationRoots[index] = GetFragmentIndex(roots[index], graph);

        StructuralCacheBoundaryIdentity[] cacheBoundaries = cacheResolution is null
            ? CreateBypassBoundaries(graph)
            : CreateResolvedBoundaries(cacheResolution, graph);

        return new StructuralPlanIdentity(
            request,
            shaderBudget,
            fragments,
            publicationRoots,
            cacheBoundaries);
    }

    public bool Equals(StructuralPlanIdentity? other)
        => other is not null
           && _request.Equals(other._request)
           && _shaderBudget.Equals(other._shaderBudget)
           && _fragments.AsSpan().SequenceEqual(other._fragments)
           && _publicationRoots.AsSpan().SequenceEqual(other._publicationRoots)
           && _cacheBoundaries.AsSpan().SequenceEqual(other._cacheBoundaries);

    public override bool Equals(object? obj)
        => obj is StructuralPlanIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_request);
        hash.Add(_shaderBudget);
        foreach (StructuralFragmentIdentity fragment in _fragments)
            hash.Add(fragment);
        foreach (int root in _publicationRoots)
            hash.Add(root);
        foreach (StructuralCacheBoundaryIdentity boundary in _cacheBoundaries)
            hash.Add(boundary);
        return hash.ToHashCode();
    }

    private static StructuralCacheBoundaryIdentity[] CreateBypassBoundaries(RecordedRenderGraph graph)
    {
        ImmutableArray<RenderCacheCandidate> candidates = graph.CacheCandidates;
        if (candidates.Length == 0)
            return [];

        var boundaries = new StructuralCacheBoundaryIdentity[candidates.Length];
        for (int index = 0; index < candidates.Length; index++)
        {
            boundaries[index] = new StructuralCacheBoundaryIdentity(
                GetFragmentIndex(candidates[index].FragmentId, graph),
                RenderCacheResolutionKind.Bypass);
        }

        return boundaries;
    }

    private static StructuralCacheBoundaryIdentity[] CreateResolvedBoundaries(
        RenderCacheResolution cacheResolution,
        RecordedRenderGraph graph)
    {
        ImmutableArray<RenderCacheDecision> decisions = cacheResolution.Decisions;
        int retained = 0;
        for (int index = 0; index < decisions.Length; index++)
        {
            if (IsBoundary(decisions[index].Kind))
                retained++;
        }

        if (retained == 0)
            return [];

        var boundaries = new StructuralCacheBoundaryIdentity[retained];
        int write = 0;
        for (int index = 0; index < decisions.Length; index++)
        {
            RenderCacheDecision decision = decisions[index];
            if (!IsBoundary(decision.Kind))
                continue;

            boundaries[write++] = new StructuralCacheBoundaryIdentity(
                GetFragmentIndex(decision.Candidate.FragmentId, graph),
                decision.Kind);
        }

        return boundaries;

        static bool IsBoundary(RenderCacheResolutionKind kind)
            => kind is RenderCacheResolutionKind.Hit or RenderCacheResolutionKind.MissCapture;
    }

    private static int GetFragmentIndex(RenderFragmentId id, RecordedRenderGraph graph)
    {
        if (id.RequestId != graph.RequestId || id.Value <= 0 || id.Value > graph.Fragments.Length)
            throw new InvalidOperationException("A structural-plan fragment ID does not belong to its graph.");
        return checked((int)id.Value - 1);
    }
}
