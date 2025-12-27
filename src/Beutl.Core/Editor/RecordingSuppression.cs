namespace Beutl.Editor;

// HistoryMangerが記録するのを抑制
public static class RecordingSuppression
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
