namespace Beutl.Graphics.Rendering;

internal readonly record struct ProgramCacheStatistics(
    long Hits,
    long Misses,
    long Creations,
    long Evictions,
    int RetainedPrograms,
    long RetainedBytes);
