namespace Beutl.Media.Proxy;

public interface IProxyJobQueue : IAsyncDisposable
{
    int MaxConcurrency { get; }

    ValueTask<ProxyJob> EnqueueAsync(
        ProxyFingerprint source,
        ProxyPreset preset,
        CancellationToken cancellationToken = default);

    IReadOnlyList<ProxyJob> Pending();

    void Cancel(Guid jobId);

    void CancelAll();

    event EventHandler<ProxyJobChangedEventArgs>? JobChanged;
}

public interface IProxyGenerator
{
    ValueTask GenerateAsync(ProxyJob job);
}

public sealed class ProxyJobChangedEventArgs : EventArgs
{
    public required ProxyJob Job { get; init; }

    public required ProxyJobChangeKind Kind { get; init; }
}

public enum ProxyJobChangeKind
{
    Enqueued,
    Started,
    Progressed,
    Succeeded,
    Failed,
    Canceled,
    Skipped,
}
