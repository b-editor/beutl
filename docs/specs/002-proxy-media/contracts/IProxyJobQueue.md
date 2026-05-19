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
    ProxyJob Enqueue(ProxyFingerprint source, ProxyPreset preset);

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
2. **Deduplication**: `Enqueue` returns the existing in-flight job for the same `(source, preset)` rather than creating a second; the returned reference is identity-equal to the first. UI relies on this to attach progress observers from multiple call sites.
3. **Atomic completion**: a job's terminal state is observable through `JobChanged` *after* `IProxyStore.Register` (on success) or after the orphan `.tmp` file has been deleted (on failure / cancel). UI never sees "succeeded but the store doesn't know about it".
4. **Cancellation semantics**: `Cancel` propagates the queue-level token to the in-flight orchestrator. On cancel, the orchestrator MUST delete any `*.tmp` files it created before completing the job. The job's terminal state is `Canceled`, not `Failed`.
5. **No FFmpeg → no progress**: if `FFmpegInstallService.IsInstalled` is false at the moment a job would start, the queue transitions the job to `Failed` with `Error.Message = "FFmpeg not installed"`, surfaces `FFmpegInstallNotifier`, and pauses the drain loop until install completes.
6. **Bounded queue**: enqueue blocks (async-await) once the backing channel is full (capacity 256). UI surfaces "queue full" if this state is observed.
7. **`AsyncDispose` semantics**: disposing waits for the active job to reach a terminal state (after cancelling it) — never abandons in-flight encoder processes.

## Test obligations (NUnit)

- Enqueue two jobs for distinct sources: both reach `Succeeded`, in arrival order. Verify `JobChanged` events fire `Enqueued → Started → Succeeded` in the right order.
- Enqueue duplicate `(source, preset)`: only one job exists; the second `Enqueue` returns the same instance.
- `Cancel` mid-job: the `*.tmp` file is removed; the job ends in `Canceled` state; the next queued job starts.
- `CancelAll` empties the queue and lands every job in `Canceled`.
- `MaxConcurrency == 1` is observable: at most one job is in `Running` state at any point in a stress test (1000 enqueues).
- Dispose with one in-flight job: completes after the job transitions terminal; no zombie worker processes.
- FFmpeg-missing path: queued jobs stay `Queued`; first job that would run transitions to `Failed`; subsequent enqueues during the missing window also fail; after install, the queue resumes draining.
