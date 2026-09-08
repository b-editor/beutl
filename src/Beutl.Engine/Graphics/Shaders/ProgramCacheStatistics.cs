namespace Beutl.Graphics.Shaders;

internal readonly record struct ProgramCacheStatistics(
    long Hits,
    long Misses,
    long Creations,
    long Evictions,
    int RetainedPrograms,
    long RetainedBytes);
