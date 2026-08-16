namespace Beutl.Services;

internal sealed class LifetimeCancellationSource : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _source = new();

    public LifetimeCancellationSource()
    {
        Token = _source.Token;
    }

    public CancellationToken Token { get; }

    public bool IsCancellationRequested => Token.IsCancellationRequested;

    public void Cancel()
    {
        lock (_gate)
        {
            _source?.Cancel();
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? source;
        lock (_gate)
        {
            source = _source;
            _source = null;
            source?.Cancel();
        }

        source?.Dispose();
    }
}
