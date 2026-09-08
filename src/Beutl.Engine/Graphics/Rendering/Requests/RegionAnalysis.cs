using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed class RegionAnalysis
{
    public RegionAnalysis(
        RenderNodeMeasurement measurement,
        Rect? targetDomain,
        Rect? requestedRegion,
        Rect finalCommitBounds,
        RequiredRegion finalCommitRegion,
        ImmutableDictionary<RenderFragmentId, RequiredRegion> fragmentRequirements,
        ImmutableDictionary<RenderFragmentId, RequiredRegion> targetAccessRequirements,
        ImmutableDictionary<RenderFragmentId, ResolvedFragmentMetadata> metadata,
        ImmutableHashSet<RenderFragmentId> backingTargetBackdropCaptures)
    {
        Measurement = measurement;
        TargetDomain = targetDomain;
        RequestedRegion = requestedRegion;
        FinalCommitBounds = finalCommitBounds;
        FinalCommitRegion = finalCommitRegion;
        FragmentRequirements = fragmentRequirements;
        TargetAccessRequirements = targetAccessRequirements;
        Metadata = metadata;
        BackingTargetBackdropCaptures = backingTargetBackdropCaptures;
    }

    public RenderNodeMeasurement Measurement { get; }

    public Rect? TargetDomain { get; }

    public Rect? RequestedRegion { get; }

    public Rect FinalCommitBounds { get; }

    public RequiredRegion FinalCommitRegion { get; }

    public ImmutableDictionary<RenderFragmentId, RequiredRegion> FragmentRequirements { get; }

    public ImmutableDictionary<RenderFragmentId, RequiredRegion> TargetAccessRequirements { get; }

    public ImmutableDictionary<RenderFragmentId, ResolvedFragmentMetadata> Metadata { get; }

    public ImmutableHashSet<RenderFragmentId> BackingTargetBackdropCaptures { get; }

    public RequiredRegion GetFragmentRequirement(RenderFragmentReference reference)
        => FragmentRequirements[GetId(reference)];

    public RequiredRegion GetTargetAccessRequirement(RenderFragmentReference reference)
        => TargetAccessRequirements.TryGetValue(GetId(reference), out RequiredRegion requirement)
            ? requirement
            : RequiredRegion.Empty;

    public ResolvedFragmentMetadata GetMetadata(RenderFragmentReference reference)
        => Metadata[GetId(reference)];

    private static RenderFragmentId GetId(RenderFragmentReference reference)
        => reference.Id
           ?? throw new InvalidOperationException("The fragment was not committed to the request graph.");
}
