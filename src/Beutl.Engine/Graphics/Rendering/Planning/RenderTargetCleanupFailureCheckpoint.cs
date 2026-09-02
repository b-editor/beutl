namespace Beutl.Graphics.Rendering.Requests;

internal readonly record struct RenderTargetCleanupFailureCheckpoint(
    RenderTargetLeaseSession Session,
    int SessionFailureCount,
    int RequestFailureCount);
