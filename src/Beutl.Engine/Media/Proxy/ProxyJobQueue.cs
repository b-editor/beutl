using System.Runtime.ExceptionServices;
using System.Threading.Channels;

using Beutl.Logging;

using Microsoft.Extensions.Logging;

namespace Beutl.Media.Proxy;

public sealed partial class ProxyJobQueue : IProxyJobQueue
{
    private static readonly ILogger s_logger = Log.CreateLogger("ProxyJobQueue");
    private static readonly TimeSpan s_defaultMinUnavailableBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan s_defaultMaxUnavailableBackoff = TimeSpan.FromSeconds(30);
    private readonly Func<IProxyGenerator?> _generatorProvider;
    private IProxyGenerator? _generator;
    private IProxyGeneratorAvailability? _generatorAvailability;
    private long _generatorVersion;
    private readonly IProxyStore? _store;
    private readonly Channel<WorkItem> _channel;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Dictionary<(ProxyFingerprint Source, ProxyPreset Preset), WorkItem> _itemsByKey = [];
    private readonly List<WorkItem> _items = [];
    private readonly Lock _lock = new();
    private readonly Task _drainTask;
    private readonly TimeSpan _minUnavailableBackoff;
    private readonly TimeSpan _maxUnavailableBackoff;
    private TaskCompletionSource? _resumeAfterGeneratorUnavailable;
    private int _consecutiveUnavailable;
    private bool _disposed;

    public ProxyJobQueue(IProxyGenerator generator)
        : this(EagerProvider(generator), store: null)
    {
    }

    public ProxyJobQueue(IProxyGenerator generator, IProxyStore? store)
        : this(EagerProvider(generator), store, s_defaultMinUnavailableBackoff, s_defaultMaxUnavailableBackoff)
    {
    }

    internal ProxyJobQueue(
        IProxyGenerator generator,
        IProxyStore? store,
        TimeSpan minUnavailableBackoff,
        TimeSpan maxUnavailableBackoff)
        : this(
            EagerProvider(generator),
            store,
            minUnavailableBackoff,
            maxUnavailableBackoff)
    {
    }

    /// <summary>
    /// Constructs the queue with a generator resolved from <paramref name="generatorProvider"/> on first dispatch, so an
    /// extension can register it later; while the provider returns null, jobs stay queued and are re-probed.
    /// </summary>
    public ProxyJobQueue(Func<IProxyGenerator?> generatorProvider, IProxyStore? store)
        : this(generatorProvider, store, s_defaultMinUnavailableBackoff, s_defaultMaxUnavailableBackoff)
    {
    }

    internal ProxyJobQueue(
        Func<IProxyGenerator?> generatorProvider,
        IProxyStore? store,
        TimeSpan minUnavailableBackoff,
        TimeSpan maxUnavailableBackoff)
    {
        ArgumentNullException.ThrowIfNull(generatorProvider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(minUnavailableBackoff, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxUnavailableBackoff, minUnavailableBackoff);

        _generatorProvider = generatorProvider;
        _store = store;
        _minUnavailableBackoff = minUnavailableBackoff;
        _maxUnavailableBackoff = maxUnavailableBackoff;
        // Unbounded: each queued item needs exactly one wake permit, and the drain (single reader,
        // MaxConcurrency 1) consumes one per dispatch. Items already live in _items — deduplicated by
        // (source, preset) — so the channel is a pure wake signal, not the memory bound. A bounded
        // channel would instead deadlock a bulk enqueue: the single drain parks on generator
        // unavailability, the channel fills, and the sequential producer blocks forever on the write.
        _channel = Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        _drainTask = Task.Run(DrainAsync);
    }

    // Wraps a concrete generator as a provider for the eager constructors; a null generator throws at construction.
    private static Func<IProxyGenerator?> EagerProvider(IProxyGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        return () => generator;
    }

    // Resolves and caches the generator on first dispatch. The drain task is the only caller, but
    // InvalidateGenerator can run concurrently from the registry-changed callback, so the shared fields
    // are guarded by _lock. The availability subscription is taken out here so an extension-registered
    // generator that loads after the queue is constructed still drives the queue's resume/backoff.
    private IProxyGenerator? ResolveGenerator()
    {
        long version;
        lock (_lock)
        {
            if (_generator is { } cached)
                return cached;

            version = _generatorVersion;
        }

        // Resolve outside _lock: the provider enumerates ProxyGeneratorRegistry (its own lock) and may
        // run plugin code, so holding _lock here risks a lock-order deadlock against InvalidateGenerator.
        IProxyGenerator? generator = _generatorProvider();
        if (generator is null)
            return null;

        lock (_lock)
        {
            if (_generator is { } raced)
                return raced;

            if (_generatorVersion != version)
                // Invalidated while we were resolving: the generator we got may be the just-removed one.
                // Discard it (do not cache/subscribe) so the next dispatch re-resolves against the
                // current registry; the drain treats null as "no generator yet" and retries.
                return null;

            _generator = generator;
            _generatorAvailability = generator as IProxyGeneratorAvailability;
            if (_generatorAvailability != null)
                _generatorAvailability.AvailabilityChanged += OnGeneratorAvailabilityChanged;
            return generator;
        }
    }

    /// <summary>
    /// Drops the cached generator so the next dispatch re-resolves it, for example after its extension unloads.
    /// Idempotent and safe to call from any thread.
    /// </summary>
    public void InvalidateGenerator()
    {
        IProxyGeneratorAvailability? previous;
        lock (_lock)
        {
            _generatorVersion++;
            previous = _generatorAvailability;
            _generator = null;
            _generatorAvailability = null;
        }

        if (previous != null)
            previous.AvailabilityChanged -= OnGeneratorAvailabilityChanged;
    }

    public int MaxConcurrency => 1;

    public event EventHandler<ProxyJobChangedEventArgs>? JobChanged;

    // Synchronous despite the ValueTask that IProxyJobQueue requires: the item is registered under the lock and the
    // channel write is a non-blocking TryWrite.
    public ValueTask<ProxyJob> EnqueueAsync(
        ProxyFingerprint source,
        ProxyPreset preset,
        int priority = 0,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var key = (source, preset);
        WorkItem? newItem = null;
        ProxyJob? existingJob = null;
        bool promoted = false;
        lock (_lock)
        {
            if (_itemsByKey.TryGetValue(key, out WorkItem? existing))
            {
                if (!IsTerminal(existing.Job.Status))
                {
                    existingJob = existing.Job;
                    if (priority > existingJob.Priority)
                    {
                        existingJob.Priority = priority;
                        promoted = true;
                    }
                }
                else
                {
                    // A terminal entry for this key stays parked in the map until its drain loop calls
                    // Remove; drop it now so the replacement can take the key without Add throwing on
                    // the duplicate. Remove's ReferenceEquals guard keeps that later call a no-op.
                    _itemsByKey.Remove(key);
                    _items.Remove(existing);
                }
            }

            if (existingJob == null)
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
                ProxyJob? job = null;
                var progress = new Progress<ProxyJobProgress>(value =>
                {
                    job!.LatestProgress = value;
                    OnJobChanged(job, ProxyJobChangeKind.Progressed);
                });
                job = new ProxyJob(
                    source,
                    preset,
                    progress,
                    cts.Token,
                    priority);

                newItem = new WorkItem(job, cts);
                _itemsByKey.Add(key, newItem);
                _items.Add(newItem);
            }
        }

        if (existingJob != null)
        {
            if (promoted)
            {
                OnJobChanged(existingJob, ProxyJobChangeKind.Enqueued);
            }
            return new ValueTask<ProxyJob>(existingJob);
        }

        WorkItem item = newItem!;
        if (cancellationToken.IsCancellationRequested || item.Token.IsCancellationRequested)
        {
            // Already canceled at enqueue (caller token or a Dispose/CancelAll that beat us here): do
            // not publish a job. Complete it first so a deduplicated caller holding this ProxyJob sees a
            // terminal transition instead of a permanent Queued.
            RetireCanceled(item);
            cancellationToken.ThrowIfCancellationRequested();
            throw new OperationCanceledException(item.Token);
        }

        // The item is already tracked in _items, so the channel entry is only the wake permit the drain
        // consumes to dispatch it. Publish before the write so a permit read cannot race an unpublished
        // item. TryWrite on the unbounded channel always succeeds and never blocks, so a bulk
        // GenerateAll cannot deadlock the caller even while the drain is parked on unavailability.
        lock (_lock)
        {
            item.Published = true;
        }

        _channel.Writer.TryWrite(item);
        OnJobChanged(item.Job, ProxyJobChangeKind.Enqueued);
        return new ValueTask<ProxyJob>(item.Job);
    }

    public IReadOnlyList<ProxyJob> Pending()
    {
        lock (_lock)
        {
            return _items
                .Select(static i => i.Job)
                .Where(static j => !IsTerminal(j.Status))
                .ToArray();
        }
    }

    // Allocation-free membership test for the eviction sweep, which queries it once per candidate: a
    // non-terminal job whose (source, preset) matches. Prefer this over Pending() there, which would
    // rebuild a filtered snapshot per candidate.
    public bool IsGenerating(ProxyFingerprint source, ProxyPreset preset)
    {
        lock (_lock)
        {
            foreach (WorkItem item in _items)
            {
                ProxyJob job = item.Job;
                if (!IsTerminal(job.Status) && job.Source == source && job.Preset == preset)
                    return true;
            }
        }

        return false;
    }

    public void Cancel(Guid jobId)
    {
        WorkItem? item;
        lock (_lock)
        {
            item = _items.FirstOrDefault(i => i.Job.JobId == jobId);
        }

        if (item != null)
            CancelItem(item);
    }

    public void CancelAll()
    {
        WorkItem[] snapshot;
        lock (_lock)
        {
            snapshot = [.. _items.Where(static i => !IsTerminal(i.Job.Status))];
        }

        foreach (WorkItem item in snapshot)
        {
            CancelItem(item);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelAll();
        _channel.Writer.TryComplete();
        _disposeCts.Cancel();

        try
        {
            await _drainTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        // Unsubscribe only after the drain loop has ended. DrainAsync is the sole place that resolves a
        // generator and subscribes (under _lock), so reading _generatorAvailability before awaiting it
        // could miss a subscription taken between the read and the drain finishing, leaking a live
        // reference to this queue. Mirror InvalidateGenerator: read + null under _lock, unsubscribe
        // outside it.
        IProxyGeneratorAvailability? availability;
        lock (_lock)
        {
            availability = _generatorAvailability;
            _generatorAvailability = null;
        }

        if (availability != null)
            availability.AvailabilityChanged -= OnGeneratorAvailabilityChanged;

        _disposeCts.Dispose();
    }

    private async Task DrainAsync()
    {
        // Each channel entry is a permit to drive one job to a terminal state; the job is chosen by
        // priority (not by which entry was read), so a high-priority enqueue can jump a bulk run.
        await foreach (WorkItem permitItem in _channel.Reader.ReadAllAsync())
        {
            // The enqueuer publishes before writing the permit, so this is normally already set;
            // re-assert it idempotently so a permit read can never observe an unpublished item.
            lock (_lock)
            {
                permitItem.Published = true;
            }

            try
            {
                await ProcessOneAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Last-resort backstop: a fault escaping the per-job guards must not kill the
                // drain loop, or every later job would sit Queued forever with no diagnostics.
                s_logger.LogError(ex, "Proxy job dispatch faulted; continuing with the next queued job.");
            }
        }

        // An item published between DisposeAsync's CancelAll snapshot and Writer.TryComplete is
        // cancellation-requested, so TakeNextDispatchable never selects it and nothing else moves
        // it out of Queued; complete such leftovers so no job ends non-terminal.
        WorkItem[] leftovers;
        lock (_lock)
        {
            leftovers = [.. _items];
        }

        foreach (WorkItem item in leftovers)
        {
            RetireCanceled(item);
        }
    }

    // Drives one channel permit to a terminal state, then loops while a dispatchable item remains.
    // Generator unavailability parks the serial drain under its bounded wait.
    private async Task ProcessOneAsync()
    {
        while (true)
        {
            WorkItem? item = TakeNextDispatchable();
            if (item == null)
                return;

            if (!item.TryStart())
            {
                RetireCanceled(item);
                return;
            }

            bool requeued = false;
            try
            {
                // ResolveGenerator runs inside the guarded region: the provider is plugin-supplied
                // and a throw must fail this job, not the drain loop.
                IProxyGenerator? generator = ResolveGenerator();
                if (generator is null)
                {
                    requeued = DeferUntilGeneratorRegistered(item);
                }
                else
                {
                    item.Job.StatusMessage = null;
                    bool generated = await GenerateAsync(generator, item).ConfigureAwait(false);
                    if (generated && item.TryCompleteSuccess())
                    {
                        Interlocked.Exchange(ref _consecutiveUnavailable, 0);
                        OnJobChanged(item.Job, ProxyJobChangeKind.Succeeded);
                    }
                    else if (generated)
                    {
                        CompleteCanceled(item);
                    }
                }
            }
            catch (Exception) when (item.Token.IsCancellationRequested)
            {
                // Cancellation owns terminal classification once requested, even when the
                // generator concurrently reports another error.
                await item.WaitForCancellationCallbacksAsync().ConfigureAwait(false);
                CompleteCanceled(item);
            }
            catch (ProxyGenerationSkippedException ex)
            {
                CompleteSkippedOrCanceled(item, ex.Message);
            }
            catch (ProxyGeneratorUnavailableException ex)
            {
                requeued = HandleGeneratorUnavailable(item, ex);
            }
            catch (Exception ex)
            {
                FailJob(item, ex, cancellationWins: true);
            }

            if (!requeued)
            {
                Remove(item);
                item.Dispose();
                return;
            }

            await WaitForGeneratorResumeOrDisposeAsync(item.Token).ConfigureAwait(false);

            if (_disposeCts.IsCancellationRequested)
                return;
        }
    }

    // Returns whether the item was requeued; otherwise it has been completed as canceled.
    private bool DeferUntilGeneratorRegistered(WorkItem item)
    {
        item.Job.Status = ProxyJobStatus.Running;
        OnJobChanged(item.Job, ProxyJobChangeKind.Started);
        // No generator has registered yet. Keep the job queued and re-probe after the same
        // bounded backoff used for unavailable generators; extension loading may register one.
        item.Job.StatusMessage = "Waiting for proxy generator registration.";
        bool requeued = RequeueForRetry(item);
        if (!requeued)
            CompleteCanceled(item);

        return requeued;
    }

    // Returns whether the item was requeued; otherwise it has reached a terminal state.
    private bool HandleGeneratorUnavailable(WorkItem item, ProxyGeneratorUnavailableException ex)
    {
        // A racing InvalidateGenerator writes null under _lock; the terminal-vs-requeue
        // decision must not act on a stale non-null read.
        bool generatorStillCached;
        bool hasAvailabilitySignal;
        lock (_lock)
        {
            generatorStillCached = _generator != null;
            hasAvailabilitySignal = _generatorAvailability != null;
        }

        if (generatorStillCached && !hasAvailabilitySignal)
        {
            // With no availability signal the queue can never learn the generator recovered,
            // so requeuing would occupy the serial queue forever (e.g. a build without FFmpeg).
            // Treat it as a terminal skip instead.
            CompleteSkippedOrCanceled(item, ex.Message);
            return false;
        }

        // Environmental unavailability, or a generator swapped mid-generate: keep the job queued and re-probe after a
        // backoff. The reason stays visible in the UI until the next dispatch clears it.
        item.Job.StatusMessage = ex.Message;
        bool requeued = RequeueForRetry(item);
        if (!requeued)
            CompleteCanceled(item);

        return requeued;
    }

    private async Task<bool> GenerateAsync(IProxyGenerator generator, WorkItem item)
    {
        ExceptionDispatchInfo? generationFailure = null;
        try
        {
            item.Token.ThrowIfCancellationRequested();
            item.Job.Status = ProxyJobStatus.Running;
            OnJobChanged(item.Job, ProxyJobChangeKind.Started);
            await generator.GenerateAsync(item.Job).ConfigureAwait(false);
            item.Token.ThrowIfCancellationRequested();
        }
        catch (Exception ex)
        {
            generationFailure = ExceptionDispatchInfo.Capture(ex);
        }

        bool successfulGenerationSealed = generationFailure is null
                                          && item.TryCloseCancellationWindow();

        if (generationFailure is not null
            && IsReportableGenerationFailure(generationFailure.SourceException, item.Token))
        {
            await PublishGenerationFailureAsync(item, generationFailure.SourceException)
                .ConfigureAwait(false);
            return false;
        }

        if (generationFailure is not null && item.Token.IsCancellationRequested)
        {
            await item.WaitForCancellationCallbacksAsync().ConfigureAwait(false);
        }

        generationFailure?.Throw();
        if (!successfulGenerationSealed)
        {
            await item.WaitForCancellationCallbacksAsync().ConfigureAwait(false);
            item.Token.ThrowIfCancellationRequested();
            throw new InvalidOperationException(
                "Proxy generation completed after its terminal transition was closed.");
        }

        return true;
    }

    // Skips, unavailability and the item's own cancellation are rethrown for ProcessOneAsync to classify;
    // any other generation failure is recorded in the store before terminal publication.
    private static bool IsReportableGenerationFailure(Exception failure, CancellationToken token)
    {
        return failure is not (ProxyGenerationSkippedException or ProxyGeneratorUnavailableException)
            && (failure is not OperationCanceledException || !token.IsCancellationRequested);
    }

    private async Task PublishGenerationFailureAsync(WorkItem item, Exception failure)
    {
        item.Job.Error = failure;
        FailureRegistration failureRegistration = RegisterFailure(
            item.Job,
            failure.Message);

        Exception? rollbackFailure = null;
        var rollbackSync = new object();
        bool failureCanPublish;
        using (item.Token.Register(() =>
               {
                   if (!item.TryClaimCancellationDuringCleanup())
                   {
                       return;
                   }

                   Exception? currentRollbackFailure = RollBackFailure(
                       item.Job,
                       failureRegistration);
                   if (currentRollbackFailure is not null)
                   {
                       lock (rollbackSync)
                       {
                           rollbackFailure = currentRollbackFailure;
                       }
                   }
               }))
        {
            failureCanPublish = item.TryCloseCancellationWindow();
            if (!failureCanPublish)
            {
                await item.WaitForCancellationCallbacksAsync().ConfigureAwait(false);
            }
        }

        Exception? synchronizedRollbackFailure;
        lock (rollbackSync)
        {
            synchronizedRollbackFailure = rollbackFailure;
        }

        if (!failureCanPublish
            || !item.TryClaimNonCancellationTerminal(cancellationWins: false))
        {
            if (synchronizedRollbackFailure is null)
            {
                item.Job.Error = null;
                item.Job.Status = ProxyJobStatus.Canceled;
                OnJobChanged(item.Job, ProxyJobChangeKind.Canceled);
                return;
            }

            item.TryClaimNonCancellationTerminal(cancellationWins: false);
            item.Job.Error = new AggregateException(
                "Proxy generation failed and cancellation rollback did not complete.",
                failure,
                synchronizedRollbackFailure);
        }

        // Observers of terminal failure can already read the Failed store entry.
        item.Job.Status = ProxyJobStatus.Failed;
        OnJobChanged(item.Job, ProxyJobChangeKind.Failed);
    }

    private bool RequeueForRetry(WorkItem item)
    {
        if (!item.ResetForRetry())
            return false;

        OnJobChanged(item.Job, ProxyJobChangeKind.Enqueued);
        return true;
    }

    private async Task WaitForGeneratorResumeOrDisposeAsync(CancellationToken jobCancellation)
    {
        Task resumeTask;
        lock (_lock)
        {
            // Retry immediately only if availability is known to have returned; otherwise back off
            // (including when the generator exposes no availability signal) to avoid a busy retry loop.
            if (_generatorAvailability is { IsAvailable: true })
                return;

            _resumeAfterGeneratorUnavailable ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            resumeTask = _resumeAfterGeneratorUnavailable.Task;
        }

        TimeSpan backoff = NextUnavailableBackoff();
        // Wake on the parked job's own cancellation too: otherwise canceling it leaves the single
        // drain loop asleep for up to the backoff (30s), blocking every later job behind it.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, jobCancellation);
        try
        {
            await resumeTask.WaitAsync(backoff, linked.Token).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Backoff elapsed with no availability signal: fall through so the drain loop re-probes
            // the generator on the next queued job, letting a transient failure self-recover.
        }
        catch (OperationCanceledException)
        {
        }
    }

    private TimeSpan NextUnavailableBackoff()
    {
        int attempt = Interlocked.Increment(ref _consecutiveUnavailable);
        return ExponentialBackoff(attempt, _minUnavailableBackoff, _maxUnavailableBackoff);
    }

    // The exponent cap keeps the factor finite over a long streak.
    private static TimeSpan ExponentialBackoff(int attempt, TimeSpan minimum, TimeSpan maximum)
    {
        double factor = Math.Pow(2, Math.Min(attempt - 1, 16));
        double milliseconds = Math.Min(maximum.TotalMilliseconds, minimum.TotalMilliseconds * factor);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private WorkItem? TakeNextDispatchable()
    {
        lock (_lock)
        {
            WorkItem? best = null;
            foreach (WorkItem candidate in _items)
            {
                if (!candidate.Published
                    || candidate.Job.Status != ProxyJobStatus.Queued
                    || candidate.Cancellation.IsCancellationRequested)
                {
                    continue;
                }

                if (best == null || candidate.Job.Priority > best.Job.Priority)
                    best = candidate;
            }

            return best;
        }
    }

    private void OnGeneratorAvailabilityChanged(object? sender, EventArgs e)
    {
        if (_generatorAvailability?.IsAvailable != true)
            return;

        TaskCompletionSource? resume;
        lock (_lock)
        {
            resume = _resumeAfterGeneratorUnavailable;
            _resumeAfterGeneratorUnavailable = null;
        }

        resume?.TrySetResult();
    }

    private void CompleteCanceled(WorkItem item)
    {
        // Concurrent callers (CancelAll, a failed enqueue write, the drain-exit sweep) can race
        // on the same item; the guarded transition makes exactly one of them win, so consumers
        // never observe a duplicate Canceled notification.
        lock (_lock)
        {
            if (IsTerminal(item.Job.Status))
                return;

            item.Job.Status = ProxyJobStatus.Canceled;
        }

        OnJobChanged(item.Job, ProxyJobChangeKind.Canceled);
    }

    private void RetireCanceled(WorkItem item)
    {
        CompleteCanceled(item);
        Remove(item);
        item.Dispose();
    }

    private void CancelItem(WorkItem item)
    {
        if (item.TryCancelQueued())
        {
            // The drain loop discards this item's channel permit and re-selects from _items, so it
            // never reaches ProcessOneAsync to be disposed; dispose here to release its linked CTS.
            RetireCanceled(item);
            return;
        }

        item.Cancel();
    }

    private void Remove(WorkItem item)
    {
        lock (_lock)
        {
            var key = (item.Job.Source, item.Job.Preset);
            if (_itemsByKey.TryGetValue(key, out WorkItem? current)
                && ReferenceEquals(current, item))
            {
                _itemsByKey.Remove(key);
            }

            _items.Remove(item);
        }
    }

    private void OnJobChanged(ProxyJob job, ProxyJobChangeKind kind)
    {
        if (JobChanged is { } handlers)
            NotifyJobChanged(new ProxyJobChangedEventArgs { Job = job, Kind = kind }, handlers);
    }

    private void NotifyJobChanged(ProxyJobChangedEventArgs args, EventHandler<ProxyJobChangedEventArgs> handlers)
    {
        foreach (EventHandler<ProxyJobChangedEventArgs> handler in Delegate.EnumerateInvocationList(handlers))
        {
            try
            {
                handler(this, args);
            }
            catch (Exception ex)
            {
                try { s_logger.LogError(ex, "A JobChanged subscriber threw for job {JobId} ({Kind}).", args.Job.JobId, args.Kind); }
                catch { }
            }
        }
    }

    private static bool IsTerminal(ProxyJobStatus status)
        => status is ProxyJobStatus.Succeeded
            or ProxyJobStatus.Failed
            or ProxyJobStatus.Canceled
            or ProxyJobStatus.Skipped;
}
