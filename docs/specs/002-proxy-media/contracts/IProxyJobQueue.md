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
    /// Queued or Running, returns that existing job instead of creating a duplicate.
    /// </summary>
    ValueTask<ProxyJob> EnqueueAsync(
        ProxyFingerprint source,
        ProxyPreset preset,
        CancellationToken cancellationToken = default);

    /// <summary>Snapshot of every Queued + Running job in arrival order.</summary>
    IReadOnlyList<ProxyJob> Pending();

    /// <summary>Cancel a specific job. No-op if the job is already terminal.</summary>
    void Cancel(Guid jobId);

    /// <summary>Cancel every Queued + Running job.</summary>
    void CancelAll();

    /// <summary>
    /// Raised on Queued → Running, Running → terminal, and job progress updates.
    /// Always raised off the consumer thread; UI subscribes via the existing dispatcher.
    /// </summary>
    event EventHandler<ProxyJobChangedEventArgs> JobChanged;
}

public sealed class ProxyJobChangedEventArgs : EventArgs
{
    public required ProxyJob Job { get; init; }
    public required ProxyJobChangeKind Kind { get; init; }
}

public enum ProxyJobChangeKind { Enqueued, Started, Progressed, Succeeded, Failed, Canceled }
```

## Behavior contract

1. **Serial execution at MVP**: `MaxConcurrency == 1`. The drain loop will not start job N+1 until job N is in a terminal state.
2. **Deduplication**: `EnqueueAsync` returns the existing in-flight job for the same `(source, preset)` rather than creating a second; the returned reference is identity-equal to the first. UI relies on this to attach progress observers from multiple call sites. Duplicate returns may complete synchronously.
3. **Atomic completion**: a job's terminal state is observable through `JobChanged` *after* `IProxyStore.Register` (on success) or after the orphan `.tmp` file has been deleted (on failure / cancel). UI never sees "succeeded but the store doesn't know about it".
4. **Cancellation semantics**: `Cancel` propagates the queue-level token to the in-flight generator. On cancel, the generator MUST delete any `*.tmp` files it created before completing the job. The job's terminal state is `Canceled`, not `Failed`.
5. **Generator unavailable → no progress**: if the registered `IProxyGenerator` reports a missing dependency (the FFmpeg implementation uses this for "FFmpeg not installed"), the queue transitions the active job to `Failed` with `Error.Message = "FFmpeg not installed"` or the generator-supplied equivalent, then pauses the drain loop until the generator reports availability. The concrete FFmpeg generator, not `Beutl.Engine`, surfaces `FFmpegInstallNotifier`.
6. **Bounded queue**: `EnqueueAsync` awaits once the backing channel is full (capacity 256). UI can pass a short cancellation token to surface "queue full" or "try again" without blocking the UI thread.
7. **`AsyncDispose` semantics**: disposing waits for the active job to reach a terminal state (after cancelling it) — never abandons in-flight encoder processes.

## Test obligations (NUnit)

- `await EnqueueAsync` for two jobs for distinct sources: both reach `Succeeded`, in arrival order. Verify `JobChanged` events fire `Enqueued → Started → Succeeded` in the right order.
- Enqueue duplicate `(source, preset)`: only one job exists; the second `EnqueueAsync` returns the same instance.
- `Cancel` mid-job: the `*.tmp` file is removed; the job ends in `Canceled` state; the next queued job starts.
- `CancelAll` empties the queue and lands every job in `Canceled`.
- `MaxConcurrency == 1` is observable: at most one job is in `Running` state at any point in a stress test (1000 enqueues).
- Dispose with one in-flight job: completes after the job transitions terminal; no zombie worker processes.
- Generator-missing path: queued jobs stay `Queued`; the active job that observes the missing dependency transitions to `Failed`; subsequent enqueues during the missing window remain queued; after the generator reports availability, the queue resumes draining.
