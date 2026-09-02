namespace Beutl.Graphics.Rendering;

internal sealed record RenderCachePlanningResult(
    RenderCacheResolution Resolution,
    IReadOnlyDictionary<RenderFragmentReference, EffectiveScale> MaterializationDemands,
    IReadOnlySet<RenderFragmentReference> MaterializedFragments,
    IReadOnlySet<RenderFragmentReference> PreviewDropEligibleMaterializations,
    int ResolutionPasses);
