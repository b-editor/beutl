namespace Beutl.Graphics.Rendering;

/// <summary>
/// Prevents a deferred execution callback from launching an unplanned renderer recursively.
/// Planned nested requests execute through <see cref="RenderRequestExecutor"/> and do not use this guard.
/// </summary>
/// <remarks>
/// The depth is thread-local rather than flowed through <see cref="ExecutionContext"/>: every guarded region
/// is entered and left on one thread, and an <see cref="AsyncLocal{T}"/> cloned the context map on each write.
/// A guard entered on one thread must therefore be released on that same thread.
/// </remarks>
internal static class RenderExecutionCallbackGuard
{
    [ThreadStatic]
    private static int t_depth;

    public static Scope Enter()
    {
        t_depth = checked(t_depth + 1);
        return new Scope(held: true);
    }

    public static bool IsActive => t_depth > 0;

    public static void ThrowIfRendererLaunchForbidden()
    {
        if (IsActive)
        {
            throw new InvalidOperationException(
                "A RenderNodeRenderer cannot be launched from a deferred render execution callback. "
                + "Record a nested render request during RenderNode.Process instead.");
        }
    }

    private static void Exit()
    {
        int depth = t_depth;
        if (depth <= 0)
            throw new InvalidOperationException("The render execution callback guard is unbalanced.");
        t_depth = depth - 1;
    }

    /// <summary>
    /// Releases one guard depth on first disposal. The struct is mutable, so it must live in a local or in a
    /// mutable field: disposing a copy releases nothing and leaks the depth.
    /// </summary>
    public struct Scope : IDisposable
    {
        private bool _held;

        internal Scope(bool held) => _held = held;

        public void Dispose()
        {
            if (!_held)
                return;

            _held = false;
            Exit();
        }
    }
}
