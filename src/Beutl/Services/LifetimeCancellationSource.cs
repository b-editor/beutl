namespace Beutl.Services;

internal sealed class LifetimeCancellationSource : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _source = new();
    private int _activeCancellationCalls;
    private bool _disposeRequested;

    public LifetimeCancellationSource()
    {
        Token = _source.Token;
    }

    public CancellationToken Token { get; }

    public bool IsCancellationRequested => Token.IsCancellationRequested;

    public void Cancel() => CancelCore(requestDispose: false);

    public void Dispose() => CancelCore(requestDispose: true);

    private void CancelCore(bool requestDispose)
    {
        CancellationTokenSource? source;
        lock (_gate)
        {
            source = _source;
            if (requestDispose)
                _disposeRequested = true;
            if (source is not null)
                _activeCancellationCalls++;
        }

        if (source is not null)
            CancelAndRelease(source);
    }

    private void CancelAndRelease(CancellationTokenSource source)
    {
        try
        {
            // User callbacks must run outside the gate so they can reenter from another thread.
            source.Cancel();
        }
        finally
        {
            bool dispose;
            lock (_gate)
            {
                _activeCancellationCalls--;
                dispose = _activeCancellationCalls == 0 && _disposeRequested;
                if (dispose)
                    _source = null;
            }

            // Dispose the source only after every admitted cancellation call has returned.
            if (dispose)
                source.Dispose();
        }
    }
}
