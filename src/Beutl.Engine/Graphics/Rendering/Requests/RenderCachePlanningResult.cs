namespace Beutl.Graphics.Rendering.Requests;

internal sealed record RenderCachePlanningResult(
    RenderCacheResolution Resolution,
    IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> MaterializationDemands,
    IReadOnlySet<RenderFragmentReference> MaterializedFragments,
    IReadOnlySet<RenderFragmentReference> PreviewDropEligibleMaterializations,
    int ResolutionPasses);
