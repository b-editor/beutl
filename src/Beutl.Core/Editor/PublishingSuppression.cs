namespace Beutl.Editor;

// IOperationObserverの通知を抑制
public static class PublishingSuppression
{
    private static readonly AsyncLocal<int> s_suppressionCount = new();

    public static bool IsSuppressed => s_suppressionCount.Value > 0;

    public static IDisposable Enter()
    {
        s_suppressionCount.Value++;
        return new SuppressionScope();
    }

    private sealed class SuppressionScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                s_suppressionCount.Value--;
                _disposed = true;
            }
        }
    }
}
