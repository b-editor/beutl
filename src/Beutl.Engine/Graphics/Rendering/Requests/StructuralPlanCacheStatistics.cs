namespace Beutl.Graphics.Rendering.Requests;

internal readonly record struct StructuralPlanCacheStatistics(
    long Hits,
    long Misses,
    long Compilations,
    long Replacements,
    int RetainedPlans);
