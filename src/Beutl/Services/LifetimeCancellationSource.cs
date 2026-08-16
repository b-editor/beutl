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
        Exception? failure = null;
        lock (_gate)
        {
            try
            {
                _source?.Cancel();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        }

        if (failure != null)
        {
            throw failure;
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? source;
        Exception? failure = null;
        lock (_gate)
        {
            source = _source;
            _source = null;
            try
            {
                source?.Cancel();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        }

        try
        {
            source?.Dispose();
        }
        finally
        {
            if (failure != null)
            {
                throw failure;
            }
        }
    }
}
