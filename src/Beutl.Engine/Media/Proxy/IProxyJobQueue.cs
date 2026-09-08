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

/// <summary>
/// Lets a host admit proxy generation only while the resources it shares with other operations
/// are available.
/// </summary>
/// <remarks>
/// Admission is a synchronous, non-blocking try operation. Returning <see langword="null"/> keeps
/// the job queued and prevents the generator from running; the queue retries after a bounded
/// per-job backoff without blocking admissible peers. A returned lease transfers to the queue and
/// is held until generation and its terminal cleanup finish. Cancellation takes precedence over a
/// concurrent admission or generation error; a lease-release failure remains a failure because
/// cleanup could not be completed.
/// </remarks>
public interface IProxyGenerationAdmission
{
    /// <summary>Attempts to acquire the host resources required by <paramref name="job"/>.</summary>
    /// <returns>
    /// A lease that the queue disposes exactly once, or <see langword="null"/> when the host is
    /// temporarily busy.
    /// </returns>
    IDisposable? TryAcquireLease(ProxyJob job);

    /// <summary>
    /// Raised when host resources may have become available after a rejected acquisition.
    /// </summary>
    /// <remarks>
    /// The queue still calls <see cref="TryAcquireLease"/> to make the admission decision. This
    /// signal only wakes deferred jobs early; bounded retry remains as a fallback for missed or
    /// unsupported host transitions.
    /// </remarks>
    event EventHandler? AvailabilityChanged;
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
    WaitingForAdmission,
    Started,
    Progressed,
    Succeeded,
    Failed,
    Canceled,
    Skipped,
}
