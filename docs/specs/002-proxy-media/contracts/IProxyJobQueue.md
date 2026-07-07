# Contract: `IProxyJobQueue`

**Feature**: 002-proxy-media | **Type**: internal extensibility surface

Background job queue for proxy generation. MVP enforces serial execution (1 active job at a time) but the contract is concurrency-parametric so a future change can lift the cap without API churn.

## C# shape

```csharp
public interface IProxyJobQueue : IAsyncDisposable
{
    int MaxConcurrency { get; }   // MVP: 1. Future: configurable.

    /// <summary>
    /// Enqueue a generation request. If a job with the same (source, preset) is already
    /// Queued or Running, returns that existing job instead of creating a duplicate
    /// (promoting its priority if the new request's is higher). Higher-priority jobs are
    /// dispatched ahead of lower-priority ones; equal priorities keep arrival (FIFO) order.
    /// An implementation that cannot express ordering may ignore priority.
    /// </summary>
    ValueTask<ProxyJob> EnqueueAsync(
        ProxyFingerprint source,
        ProxyPreset preset,
        int priority = 0,
        CancellationToken cancellationToken = default);

    /// <summary>Snapshot of every Queued + Running job in arrival order.</summary>
    IReadOnlyList<ProxyJob> Pending();

    /// <summary>Cancel a specific job. No-op if the job is already terminal.</summary>
    void Cancel(Guid jobId);

    /// <summary>Cancel every Queued + Running job.</summary>
    void CancelAll();

    /// <summary>
    /// Raised on Queued → Running, Running → terminal, skipped jobs, and job progress updates.
    /// Always raised off the consumer thread; UI subscribes via the existing dispatcher.
    /// </summary>
    event EventHandler<ProxyJobChangedEventArgs> JobChanged;
}

public sealed class ProxyJobChangedEventArgs : EventArgs
{
    public required ProxyJob Job { get; init; }
    public required ProxyJobChangeKind Kind { get; init; }
}

public enum ProxyJobChangeKind { Enqueued, Started, Progressed, Succeeded, Failed, Canceled, Skipped }

public interface IProxyGenerator
{
    /// <summary>
    /// Generate the proxy described by the job. Cancellation is carried by
    /// ProxyJob.CancellationToken so there is only one canonical cancellation source.
    /// </summary>
    ValueTask GenerateAsync(ProxyJob job);
}
```

## Behavior contract

1. **Serial execution at MVP**: `MaxConcurrency == 1`. The drain loop will not start the next job until the current one is in a terminal state. Among dispatchable jobs the highest priority runs first (a foreground per-clip generate jumps a bulk sweep); equal priorities keep arrival order. `Pending()` still reports arrival order.
2. **Deduplication**: `EnqueueAsync` returns the existing in-flight job for the same `(source, preset)` rather than creating a second; the returned reference is identity-equal to the first. UI relies on this to attach progress observers from multiple call sites. Duplicate returns may complete synchronously.
3. **Atomic completion**: a job's terminal state is observable through `JobChanged` *after* `IProxyStore.Register` (on success) or after the orphan `.tmp` file has been deleted (on failure / cancel). UI never sees "succeeded but the store doesn't know about it".
4. **Cancellation semantics**: `Cancel` propagates the queue-level token to the in-flight generator. On cancel, the generator MUST delete any `*.tmp` files it created before completing the job. The job's terminal state is `Canceled`, not `Failed`; cancel never records `ProxyState.Failed` or a failure reason.
5. **Skipped semantics**: ineligible sources (audio-only, procedural/generative, still images) complete as `ProxyJobStatus.Skipped` and raise `ProxyJobChangeKind.Skipped` with a human-readable `StatusMessage`. Skipped jobs do not call `IProxyStore.Register`, do not create a proxy entry, and leave any existing proxy entry unchanged.
6. **Generator unavailable → bounded retry or terminal skip**: if the registered `IProxyGenerator` throws `ProxyGeneratorUnavailableException` (the FFmpeg implementation uses this for "FFmpeg not installed"), the behavior depends on whether the generator exposes an availability signal (`IProxyGeneratorAvailability`). With a signal, the job stays `Queued` and the drain loop re-probes after a bounded exponential backoff, resuming immediately when the generator reports availability — the job (and its install prompt) stays alive. Without a signal the queue can never learn the generator recovered, so the job completes as a terminal `Skipped` with the generator's message. The concrete FFmpeg generator, not `Beutl.Engine`, surfaces `FFmpegInstallNotifier`.
7. **Bounded queue**: `EnqueueAsync` awaits once the backing channel is full (capacity 256). UI can pass a short cancellation token to surface "queue full" or "try again" without blocking the UI thread. If the enqueue write itself fails (caller cancellation, queue disposal), the job transitions to a terminal `Canceled` state and raises `JobChanged` before the exception propagates — a deduplicated caller holding the same `ProxyJob` never sees it stuck at `Queued` forever.
8. **Subscriber isolation**: `JobChanged` handlers are invoked individually; a throwing subscriber is logged and neither faults the queue's drain loop nor starves the remaining subscribers of the notification.
9. **`AsyncDispose` semantics**: disposing waits for the active job to reach a terminal state (after cancelling it) — never abandons in-flight encoder processes. Every job ever surfaced (returned from `EnqueueAsync` or observed via `JobChanged`) is terminal by the time `DisposeAsync` completes.

## Test obligations (NUnit)

- `await EnqueueAsync` for two jobs for distinct sources: both reach `Succeeded`, in arrival order. Verify `JobChanged` events fire `Enqueued → Started → Succeeded` in the right order.
- Enqueue duplicate `(source, preset)`: only one job exists; the second `EnqueueAsync` returns the same instance.
- `Cancel` mid-job: the `*.tmp` file is removed; the job ends in `Canceled` state; the next queued job starts.
- `CancelAll` empties the queue and lands every job in `Canceled`.
- Ineligible media path: the job ends in `Skipped`, raises `ProxyJobChangeKind.Skipped`, carries a human-readable `StatusMessage`, creates no proxy file, and leaves the store unchanged.
- `MaxConcurrency == 1` is observable: at most one job is in `Running` state at any point in a stress test (1000 enqueues).
- Dispose with one in-flight job: completes after the job transitions terminal; no zombie worker processes.
- Generator-missing path (with availability signal): the job that observes the missing dependency stays `Queued`; subsequent enqueues during the missing window remain queued; after the generator reports availability, the queue resumes draining. Without an availability signal the job completes as `Skipped`.
