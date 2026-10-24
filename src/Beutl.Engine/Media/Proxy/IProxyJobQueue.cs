namespace Beutl.Media.Proxy;

public interface IProxyJobQueue : IAsyncDisposable
{
    int MaxConcurrency { get; }

    /// <summary>
    /// Enqueues a proxy job at the given <paramref name="priority"/>. Higher-priority jobs should be
    /// dispatched ahead of lower-priority ones, and equal priorities should keep arrival (FIFO) order;
    /// an implementation that cannot express ordering may ignore <paramref name="priority"/>.
    /// </summary>
    ValueTask<ProxyJob> EnqueueAsync(
        ProxyFingerprint source,
        ProxyPreset preset,
        int priority = 0,
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

public interface IProxyGeneratorAvailability
{
    bool IsAvailable { get; }

    event EventHandler? AvailabilityChanged;
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
