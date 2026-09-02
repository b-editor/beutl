namespace Beutl.Graphics.Rendering;

internal readonly record struct RenderTargetPoolStatistics(
    long Creates,
    long Reuses,
    long Misses,
    long Evictions,
    int OwnedTargets,
    int AvailableTargets,
    int LeasedTargets,
    long OwnedBytes,
    long RetainedBytes,
    int PeakLiveTargets);
