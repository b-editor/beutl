namespace Beutl.Editor.Infrastructure;

public static class PublishingSuppression
{
    private static readonly AsyncLocal<bool> s_isSuppressed = new();

    public static bool IsSuppressed => s_isSuppressed.Value;

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
