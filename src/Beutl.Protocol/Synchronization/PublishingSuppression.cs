namespace Beutl.Protocol.Synchronization;

/// <summary>
/// Provides a mechanism to suppress operation publishing during remote operation application.
/// This prevents echo-back loops where applying a remote operation triggers local publishing.
/// </summary>
public static class PublishingSuppression
{
    private static readonly AsyncLocal<bool> s_isSuppressed = new();

    /// <summary>
    /// Gets whether publishing is currently suppressed on this thread/async context.
    /// </summary>
    public static bool IsSuppressed => s_isSuppressed.Value;

    /// <summary>
    /// Enters a suppression scope. Publishing should be disabled while in this scope.
    /// </summary>
    /// <returns>A disposable that exits the suppression scope when disposed.</returns>
    public static IDisposable Enter()
    {
        s_isSuppressed.Value = true;
        return new SuppressionScope();
    }

    private sealed class SuppressionScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                s_isSuppressed.Value = false;
                _disposed = true;
            }
        }
    }
}
