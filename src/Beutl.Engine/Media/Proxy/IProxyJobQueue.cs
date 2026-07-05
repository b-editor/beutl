namespace Beutl.Media.Proxy;

public interface IProxyJobQueue : IAsyncDisposable
{
    int MaxConcurrency { get; }

    ValueTask<ProxyJob> EnqueueAsync(
        ProxyFingerprint source,
        ProxyPreset preset,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a proxy job at the given <paramref name="priority"/>. Higher-priority jobs should be
    /// dispatched ahead of lower-priority ones, and equal priorities should keep arrival (FIFO) order.
    /// The default implementation ignores priority and forwards to the arrival-order overload, so an
    /// implementation only overrides this when it can honor priority.
    /// </summary>
    ValueTask<ProxyJob> EnqueueAsync(
        ProxyFingerprint source,
        ProxyPreset preset,
        int priority,
        CancellationToken cancellationToken = default)
        => EnqueueAsync(source, preset, cancellationToken);

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
