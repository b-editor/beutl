namespace Beutl.Editor;

// A suppression depth that flows with the async context; each scope it hands out undoes one Enter.
internal sealed class AsyncSuppressionCounter
{
    private readonly AsyncLocal<int> _suppressionCount = new();

    public bool IsSuppressed => _suppressionCount.Value > 0;

    public IDisposable Enter()
    {
        _suppressionCount.Value++;
        return new SuppressionScope(this);
    }

    private sealed class SuppressionScope(AsyncSuppressionCounter owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                owner._suppressionCount.Value--;
                _disposed = true;
            }
        }
    }
}
