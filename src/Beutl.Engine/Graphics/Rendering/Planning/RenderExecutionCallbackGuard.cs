namespace Beutl.Graphics.Rendering;

/// <summary>
/// Prevents a deferred execution callback from launching an unplanned renderer recursively.
/// Planned nested requests execute through <see cref="RenderRequestExecutor"/> and do not use this guard.
/// </summary>
internal static class RenderExecutionCallbackGuard
{
    private static readonly AsyncLocal<int> s_depth = new();

    public static IDisposable Enter()
    {
        s_depth.Value = checked(s_depth.Value + 1);
        return new Scope();
    }

    public static bool IsActive => s_depth.Value > 0;

    public static void ThrowIfRendererLaunchForbidden()
    {
        if (IsActive)
        {
            throw new InvalidOperationException(
                "A RenderNodeRenderer cannot be launched from a deferred render execution callback. "
                + "Record a nested render request during RenderNode.Process instead.");
        }
    }

    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            int depth = s_depth.Value;
            if (depth <= 0)
                throw new InvalidOperationException("The render execution callback guard is unbalanced.");
            s_depth.Value = depth - 1;
        }
    }
}
