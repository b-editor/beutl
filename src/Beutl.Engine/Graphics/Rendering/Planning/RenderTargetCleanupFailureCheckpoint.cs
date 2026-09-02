namespace Beutl.Graphics.Rendering;

internal readonly record struct RenderTargetCleanupFailureCheckpoint(
    RenderTargetLeaseSession Session,
    int SessionFailureCount,
    int RequestFailureCount);
