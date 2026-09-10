using System.Runtime.ExceptionServices;
using System.Threading.Channels;

using Beutl.Logging;

using Microsoft.Extensions.Logging;

namespace Beutl.Media.Proxy;

public sealed class ProxyJobQueue : IProxyJobQueue
{
    private readonly record struct FailureRegistration(ProxyEntry? Previous, bool Changed);

    private static readonly ILogger s_logger = Log.CreateLogger("ProxyJobQueue");
    private readonly Func<IProxyGenerator?> _generatorProvider;
    private IProxyGenerator? _generator;
    private IProxyGeneratorAvailability? _generatorAvailability;
    private long _generatorVersion;
    private readonly IProxyStore? _store;
    private readonly Channel<WorkItem> _channel;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Dictionary<(ProxyFingerprint Source, ProxyPreset Preset), WorkItem> _itemsByKey = [];
    private readonly List<WorkItem> _items = [];
    private readonly HashSet<Task> _admissionRetryTasks = [];
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Queue<(ProxyJobChangedEventArgs Args, EventHandler<ProxyJobChangedEventArgs> Handlers)>> _admissionNotifications = [];
    private readonly Task _drainTask;
    private readonly TimeSpan _minUnavailableBackoff;
    private readonly TimeSpan _maxUnavailableBackoff;
    private readonly IProxyGenerationAdmission? _admission;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private TaskCompletionSource? _resumeAfterGeneratorUnavailable;
    private int _consecutiveUnavailable;
    private bool _disposed;

    public ProxyJobQueue(IProxyGenerator generator)
        : this(EagerProvider(generator), store: null)
    {
    }

    public ProxyJobQueue(IProxyGenerator generator, IProxyStore? store)
        : this(EagerProvider(generator), store, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30))
    {
    }

    /// <summary>
    /// Constructs a queue whose generator runs only while <paramref name="admission"/> grants a
    /// lease.
    /// </summary>
    public ProxyJobQueue(
        IProxyGenerator generator,
        IProxyStore? store,
        IProxyGenerationAdmission admission)
        : this(
            EagerProvider(generator),
            store,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(30),
            admission ?? throw new ArgumentNullException(nameof(admission)))
    {
    }

    internal ProxyJobQueue(
        IProxyGenerator generator,
        IProxyStore? store,
        TimeSpan minUnavailableBackoff,
        TimeSpan maxUnavailableBackoff,
        IProxyGenerationAdmission? admission = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
        : this(
            EagerProvider(generator),
            store,
            minUnavailableBackoff,
            maxUnavailableBackoff,
            admission,
            delayAsync)
    {
    }

    /// <summary>
    /// Constructs the queue with a generator resolved lazily from <paramref name="generatorProvider"/>
    /// on the first job dispatch. Supports composition roots where the generator is registered after
    /// the queue is constructed (e.g. a proxy generator registered by an extension's <c>Load</c>,
    /// which runs after the app's <c>RegisterServices</c> builds this queue). The provider may return
    /// null to signal "not yet registered"; such jobs stay queued and the drain loop re-probes, so a
    /// job queued before extension registration is not lost.
    /// </summary>
    public ProxyJobQueue(Func<IProxyGenerator?> generatorProvider, IProxyStore? store)
        : this(generatorProvider, store, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30))
    {
    }

    /// <summary>
    /// Constructs a lazy-generator queue whose generator runs only while
    /// <paramref name="admission"/> grants a lease.
    /// </summary>
    public ProxyJobQueue(
        Func<IProxyGenerator?> generatorProvider,
        IProxyStore? store,
        IProxyGenerationAdmission admission)
        : this(
            generatorProvider,
            store,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(30),
            admission ?? throw new ArgumentNullException(nameof(admission)))
    {
    }

    internal ProxyJobQueue(
        Func<IProxyGenerator?> generatorProvider,
        IProxyStore? store,
        TimeSpan minUnavailableBackoff,
        TimeSpan maxUnavailableBackoff,
        IProxyGenerationAdmission? admission = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        ArgumentNullException.ThrowIfNull(generatorProvider);
        if (admission is null && delayAsync is not null)
        {
            throw new ArgumentException(
                "A custom admission delay requires an admission policy.",
                nameof(delayAsync));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(minUnavailableBackoff, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxUnavailableBackoff, minUnavailableBackoff);

        _generatorProvider = generatorProvider;
        _store = store;
        _minUnavailableBackoff = minUnavailableBackoff;
        _maxUnavailableBackoff = maxUnavailableBackoff;
        _admission = admission;
        if (_admission is not null)
        {
            _admission.AvailabilityChanged += OnAdmissionAvailabilityChanged;
        }
        _delayAsync = delayAsync ?? (static (delay, token) => Task.Delay(delay, token));
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

    // Wraps a concrete generator as a provider for the eager constructors. The null check runs in
    // the :this(...) initializer evaluation, preserving the pre-lazy throw-at-construction contract.
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
    /// Drops the cached generator so the next dispatch re-resolves it from the provider. A composition
    /// root calls this when its provider's result changes — e.g. a proxy extension unloads and its
    /// factory leaves <see cref="ProxyGeneratorRegistry"/> — so the queue neither keeps a strong
    /// reference to the removed generator (blocking the extension's collection) nor keeps invoking it.
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

    // Synchronous despite the ValueTask return: the item is registered under the lock and the channel
    // write is a non-blocking TryWrite, so there is nothing to await. The Async name and ValueTask
    // signature are kept to satisfy IProxyJobQueue and leave room for a future awaiting implementation.
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
        WorkItem? promotedItem = null;
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
                        promotedItem = existing;
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
                if (promotedItem!.TryGetAdmissionWait(out long generation, out _))
                    promotedItem.SignalAdmissionAvailability(generation);
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
            CompleteCanceled(item);
            Remove(item);
            item.Dispose();
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
        if (_admission is not null)
        {
            _admission.AvailabilityChanged -= OnAdmissionAvailabilityChanged;
        }

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

        Task[] admissionRetries;
        lock (_lock)
        {
            admissionRetries = [.. _admissionRetryTasks];
        }

        await Task.WhenAll(admissionRetries).ConfigureAwait(false);

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
            CompleteCanceled(item);
            Remove(item);
            item.Dispose();
        }
    }

    // Drives one channel permit to a terminal state, then loops while a dispatchable item remains.
    // Generator unavailability parks the serial drain under its bounded wait. Admission rejection
    // instead defers only that item and schedules a future permit, allowing an admissible peer to
    // run immediately.
    private async Task ProcessOneAsync()
    {
        while (true)
        {
            WorkItem? item = TakeNextDispatchable();
            if (item == null)
                return;

            if (!item.TryStart())
            {
                CompleteCanceled(item);
                Remove(item);
                item.Dispose();
                return;
            }

            bool requeued = false;
            bool admissionRejected = false;
            try
            {
                // ResolveGenerator runs inside the guarded region: the provider is plugin-supplied
                // and a throw must fail this job, not the drain loop.
                IProxyGenerator? generator = ResolveGenerator();
                if (generator is null)
                {
                    item.Job.Status = ProxyJobStatus.Running;
                    OnJobChanged(item.Job, ProxyJobChangeKind.Started);
                    // No generator has registered yet. Keep the job queued and re-probe after the same
                    // bounded backoff used for unavailable generators; extension loading may register one.
                    item.Job.StatusMessage = "Waiting for proxy generator registration.";
                    requeued = RequeueForRetry(item);
                    if (!requeued)
                        CompleteCanceled(item);
                }
                else
                {
                    IDisposable? admissionLease = _admission?.TryAcquireLease(item.Job);
                    if (_admission is not null && admissionLease is null)
                    {
                        item.Job.StatusMessage = "Waiting for proxy generation admission.";
                        admissionRejected = true;
                        requeued = RequeueForAdmissionRetry(item);
                        if (!requeued)
                            CompleteCanceled(item);
                    }
                    else
                    {
                        item.ResetAdmissionRejections();
                        item.Job.StatusMessage = null;
                        bool generated = await GenerateWithAdmissionLeaseAsync(
                                generator,
                                item,
                                admissionLease)
                            .ConfigureAwait(false);
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
            }
            catch (AdmissionLeaseReleaseException ex)
            {
                FailJob(item, ex.Failure, cancellationWins: false);
            }
            catch (Exception) when (item.Token.IsCancellationRequested)
            {
                // Cancellation owns terminal classification once requested, even when an
                // admission policy or generator concurrently reports another error. Lease-release
                // failures are handled above because failed cleanup cannot be reported as safe
                // cancellation.
                await item.WaitForCancellationCallbacksAsync().ConfigureAwait(false);
                CompleteCanceled(item);
            }
            catch (ProxyGenerationSkippedException ex)
            {
                CompleteSkippedOrCanceled(item, ex.Message);
            }
            catch (ProxyGeneratorUnavailableException ex)
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
                }
                else
                {
                    // Either the unavailability is environmental (a signal exists to learn recovery),
                    // or InvalidateGenerator raced mid-generate — a generator swap/unload, where the
                    // next dispatch re-resolves the provider like the not-yet-registered path. Both
                    // keep the job Queued and re-probe after a bounded backoff instead of dropping it.
                    // Carry the reason so the UI can distinguish "blocked on availability" from a
                    // plain backlog; the next dispatch clears it before GenerateAsync.
                    item.Job.StatusMessage = ex.Message;
                    requeued = RequeueForRetry(item);
                    if (!requeued)
                        CompleteCanceled(item);
                }
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

            if (admissionRejected)
            {
                // This item is deferred until its own bounded retry task republishes a permit.
                // Continue immediately so another dispatchable job can run instead of sitting
                // behind a job-aware policy that keeps rejecting this one.
                continue;
            }

            await WaitForGeneratorResumeOrDisposeAsync(item.Token).ConfigureAwait(false);

            if (_disposeCts.IsCancellationRequested)
                return;
        }
    }

    private async Task<bool> GenerateWithAdmissionLeaseAsync(
        IProxyGenerator generator,
        WorkItem item,
        IDisposable? admissionLease)
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
            && generationFailure.SourceException is not (ProxyGenerationSkippedException
                or ProxyGeneratorUnavailableException)
            && (generationFailure.SourceException is not OperationCanceledException
                || !item.Token.IsCancellationRequested))
        {
            Exception failure = generationFailure.SourceException;
            item.Job.Error = failure;
            // Store bookkeeping is part of terminal cleanup and remains inside admission.
            FailureRegistration failureRegistration = RegisterFailure(
                item.Job,
                failure.Message);

            Exception? rollbackFailure = null;
            var rollbackSync = new object();
            Exception? terminalReleaseFailure;
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
                terminalReleaseFailure = ReleaseAdmissionLease(admissionLease);
                failureCanPublish = terminalReleaseFailure is not null
                                    || item.TryCloseCancellationWindow();
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

            if (terminalReleaseFailure is not null)
            {
                item.TryClaimNonCancellationTerminal(cancellationWins: false);
                item.Job.Error = new AggregateException(
                    "Proxy generation and admission-lease release both failed.",
                    failure,
                    terminalReleaseFailure);
                if (item.Token.IsCancellationRequested && failureRegistration.Changed)
                {
                    RegisterFailure(item.Job, item.Job.Error.Message);
                }
            }
            else if (!failureCanPublish
                     || !item.TryClaimNonCancellationTerminal(cancellationWins: false))
            {
                if (synchronizedRollbackFailure is null)
                {
                    item.Job.Error = null;
                    item.Job.Status = ProxyJobStatus.Canceled;
                    OnJobChanged(item.Job, ProxyJobChangeKind.Canceled);
                    return false;
                }

                item.TryClaimNonCancellationTerminal(cancellationWins: false);
                item.Job.Error = new AggregateException(
                    "Proxy generation failed and cancellation rollback did not complete.",
                    failure,
                    synchronizedRollbackFailure);
            }

            // Publish only after admission is released; observers can now safely start conflicting
            // work and still see the already-recorded Failed store entry.
            item.Job.Status = ProxyJobStatus.Failed;
            OnJobChanged(item.Job, ProxyJobChangeKind.Failed);
            return false;
        }

        Exception? releaseFailure = ReleaseAdmissionLease(admissionLease);

        if (generationFailure is not null && releaseFailure is not null)
        {
            throw new AdmissionLeaseReleaseException(
                new AggregateException(
                    "Proxy generation and admission-lease release both failed.",
                    generationFailure.SourceException,
                    releaseFailure));
        }

        if (releaseFailure is not null)
        {
            throw new AdmissionLeaseReleaseException(releaseFailure);
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

    private static Exception? ReleaseAdmissionLease(IDisposable? admissionLease)
    {
        try
        {
            admissionLease?.Dispose();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private bool RequeueForRetry(WorkItem item)
    {
        if (!item.ResetForRetry())
            return false;

        OnJobChanged(item.Job, ProxyJobChangeKind.Enqueued);
        return true;
    }

    private bool RequeueForAdmissionRetry(WorkItem item)
    {
        if (!item.ResetForAdmissionRetry())
            return false;

        TimeSpan backoff = item.NextAdmissionBackoff(
            _minUnavailableBackoff,
            _maxUnavailableBackoff,
            out bool firstRejection);
        if (firstRejection)
        {
            if (!item.TryPublishAdmissionWaiting(() =>
                    OnJobChanged(item.Job, ProxyJobChangeKind.WaitingForAdmission)))
            {
                return false;
            }
        }

        Task retry = ResumeAdmissionAfterBackoffAsync(item, backoff);
        lock (_lock)
        {
            _admissionRetryTasks.Add(retry);
        }

        _ = RemoveAdmissionRetryWhenCompleteAsync(retry);
        return true;
    }

    private void OnAdmissionAvailabilityChanged(object? sender, EventArgs e)
    {
        List<(WorkItem Item, long Generation)> deferred = [];
        WorkItem[] items;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            items = [.. _items];
        }

        foreach (WorkItem item in items)
        {
            if (item.TryGetAdmissionWait(out long generation, out _))
            {
                deferred.Add((item, generation));
            }
        }

        foreach ((WorkItem item, long generation) in deferred)
        {
            item.SignalAdmissionAvailability(generation);
        }
    }

    private async Task ResumeAdmissionAfterBackoffAsync(WorkItem item, TimeSpan backoff)
    {
        if (!item.TryGetAdmissionWait(out long generation, out Task availability))
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            _disposeCts.Token,
            item.Token);
        try
        {
            Task delay = _delayAsync(backoff, linked.Token);
            Task completed = await Task.WhenAny(delay, availability)
                .WaitAsync(linked.Token)
                .ConfigureAwait(false);
            if (ReferenceEquals(completed, availability))
            {
                linked.Cancel();
                try
                {
                    await delay.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                }
            }
            else
            {
                await delay.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // The production delay is Task.Delay; this guard keeps a host/test scheduler failure
            // from silently stranding the job forever.
            s_logger.LogError(ex, "Proxy admission retry delay failed; retrying immediately.");
        }

        if (item.TryResumeAdmission(generation))
        {
            _channel.Writer.TryWrite(item);
        }
    }

    private async Task RemoveAdmissionRetryWhenCompleteAsync(Task retry)
    {
        await retry.ConfigureAwait(false);
        lock (_lock)
        {
            _admissionRetryTasks.Remove(retry);
        }
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
        double factor = Math.Pow(2, Math.Min(attempt - 1, 16));
        double ms = Math.Min(_maxUnavailableBackoff.TotalMilliseconds, _minUnavailableBackoff.TotalMilliseconds * factor);
        return TimeSpan.FromMilliseconds(ms);
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
                    || candidate.IsAdmissionDeferred
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

    private void CancelItem(WorkItem item)
    {
        if (item.TryCancelQueued())
        {
            CompleteCanceled(item);
            Remove(item);
            // The drain loop discards this item's channel permit and re-selects from _items, so it
            // never reaches ProcessOneAsync to be disposed; dispose here to release its linked CTS.
            item.Dispose();
            return;
        }

        item.Cancel();
    }

    private FailureRegistration RegisterFailure(ProxyJob job, string? failureReason)
    {
        if (_store == null)
            return default;

        ProxyEntry? previous;
        try
        {
            previous = _store.TryGet(job.Source, job.Preset);
        }
        catch (Exception ex)
        {
            RecordBookkeepingFailure(job, ex);
            return default;
        }

        if (previous is { State: ProxyState.Ready or ProxyState.Stale })
            return default;

        try
        {
            var now = DateTime.UtcNow;
            _store.Register(new ProxyEntry(
                job.Source,
                job.Preset,
                ProxyState.Failed,
                ProxyPathUtilities.BuildRelativePath(job.Source, job.Preset),
                0,
                default,
                default,
                now,
                now,
                failureReason));
            return new FailureRegistration(previous, Changed: true);
        }
        catch (Exception ex)
        {
            RecordBookkeepingFailure(job, ex);
            // Register may update memory before persistence throws. Treat the result as ambiguous
            // so a cancellation path restores the captured entry or deletes the attempted one.
            return new FailureRegistration(previous, Changed: true);
        }
    }

    private static void RecordBookkeepingFailure(ProxyJob job, Exception failure)
    {
        job.BookkeepingError = job.BookkeepingError is null
            ? failure
            : new AggregateException(job.BookkeepingError, failure);
        s_logger.LogError(
            failure,
            "Failed to record Failed proxy entry for {Source} ({Preset}).",
            job.Source.AbsolutePath,
            job.Preset);
    }

    private Exception? RollBackFailure(ProxyJob job, FailureRegistration registration)
    {
        if (_store is null || !registration.Changed)
        {
            return null;
        }

        try
        {
            if (registration.Previous is { } previous)
            {
                _store.Register(previous);
            }
            else
            {
                _store.Delete(job.Source, job.Preset);
            }

            ProxyEntry? restored = _store.TryGet(job.Source, job.Preset);
            if (!Equals(restored, registration.Previous))
            {
                throw new InvalidOperationException(
                    "Proxy failure bookkeeping could not be restored after cancellation.");
            }

            return null;
        }
        catch (Exception ex)
        {
            job.BookkeepingError = job.BookkeepingError is null
                ? ex
                : new AggregateException(job.BookkeepingError, ex);
            s_logger.LogError(
                ex,
                "Failed to roll back the proxy failure entry after cancellation for {Source} ({Preset}).",
                job.Source.AbsolutePath,
                job.Preset);
            return ex;
        }
    }

    private void CompleteSkippedOrCanceled(WorkItem item, string message)
    {
        if (!item.TryClaimNonCancellationTerminal(cancellationWins: true))
        {
            CompleteCanceled(item);
            return;
        }

        item.Job.Status = ProxyJobStatus.Skipped;
        item.Job.StatusMessage = message;
        OnJobChanged(item.Job, ProxyJobChangeKind.Skipped);
    }

    private void FailJob(WorkItem item, Exception failure, bool cancellationWins)
    {
        if (!item.TryClaimNonCancellationTerminal(cancellationWins))
        {
            CompleteCanceled(item);
            return;
        }

        // Record the Failed store entry before the terminal transition so an observer that sees
        // Status == Failed can already read the entry from the store.
        item.Job.Error = failure;
        RegisterFailure(item.Job, failure.Message);
        item.Job.Status = ProxyJobStatus.Failed;
        OnJobChanged(item.Job, ProxyJobChangeKind.Failed);
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

    private bool OnJobChanged(ProxyJob job, ProxyJobChangeKind kind)
    {
        if (kind is ProxyJobChangeKind.WaitingForAdmission or ProxyJobChangeKind.Canceled)
        {
            Queue<(ProxyJobChangedEventArgs Args, EventHandler<ProxyJobChangedEventArgs> Handlers)> notifications;
            lock (_lock)
            {
                // The same gate orders cancellation's terminal transition and the wait
                // notification reservation. Callbacks themselves never run under it.
                if (kind == ProxyJobChangeKind.WaitingForAdmission && IsTerminal(job.Status))
                    return false;
                if (JobChanged is not { } handlers)
                    return true;
                var notification = (new ProxyJobChangedEventArgs { Job = job, Kind = kind }, handlers);
                if (_admissionNotifications.TryGetValue(job.JobId, out notifications!))
                {
                    notifications.Enqueue(notification);
                    return true;
                }
                // An unrelated job's canceled callback must not defer this wait notification
                // beyond the serial drain's next attempt to start or complete this job.
                notifications = new();
                notifications.Enqueue(notification);
                _admissionNotifications.Add(job.JobId, notifications);
            }
            while (true)
            {
                (ProxyJobChangedEventArgs Args, EventHandler<ProxyJobChangedEventArgs> Handlers) notification;
                lock (_lock)
                {
                    if (!notifications.TryDequeue(out notification))
                    {
                        _admissionNotifications.Remove(job.JobId);
                        return true;
                    }
                }
                NotifyJobChanged(notification.Args, notification.Handlers);
            }
        }
        if (JobChanged is { } directHandlers)
            NotifyJobChanged(new ProxyJobChangedEventArgs { Job = job, Kind = kind }, directHandlers);
        return true;
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

    private sealed class AdmissionLeaseReleaseException(Exception failure)
        : Exception("The proxy-generation admission lease could not be released.", failure)
    {
        public Exception Failure { get; } = failure;
    }

    private sealed class WorkItem(ProxyJob job, CancellationTokenSource cancellation) : IDisposable
    {
        private readonly Lock _lock = new();
        private bool _started;
        private volatile bool _admissionDeferred;
        private TaskCompletionSource? _admissionAvailability;
        private TaskCompletionSource? _cancellationCallbacksCompleted;
        private long _admissionGeneration;
        private bool _terminalTransitionClaimed;
        private bool _cancellationRequested;
        private bool _cancellationWindowClosed;
        private bool _disposed;
        private int _consecutiveAdmissionRejections;

        public ProxyJob Job { get; } = job;

        // Guarded by the queue's _lock (not this WorkItem's _lock). Set true once the item is fully
        // registered, immediately before its wake permit is written; TakeNextDispatchable ignores
        // unpublished items so a partially-constructed job can never be dispatched.
        public bool Published { get; set; }

        public CancellationTokenSource Cancellation { get; } = cancellation;

        // Captured up front so a waiter can observe cancellation even after the source is disposed
        // (reading Cancellation.Token after Dispose throws); the token value stays valid.
        public CancellationToken Token { get; } = cancellation.Token;

        public bool TryStart()
        {
            lock (_lock)
            {
                if (_disposed
                    || _admissionDeferred
                    || _terminalTransitionClaimed
                    || _cancellationRequested
                    || Cancellation.IsCancellationRequested)
                    return false;

                _started = true;
                return true;
            }
        }

        public bool ResetForRetry()
        {
            lock (_lock)
            {
                if (_disposed
                    || _terminalTransitionClaimed
                    || _cancellationRequested
                    || Cancellation.IsCancellationRequested
                    || IsTerminal(Job.Status))
                    return false;

                _started = false;
                _admissionDeferred = false;
                Job.Status = ProxyJobStatus.Queued;
                return true;
            }
        }

        public bool ResetForAdmissionRetry()
        {
            lock (_lock)
            {
                if (_disposed
                    || _terminalTransitionClaimed
                    || _cancellationRequested
                    || Cancellation.IsCancellationRequested
                    || IsTerminal(Job.Status))
                    return false;

                _started = false;
                _admissionDeferred = true;
                _admissionGeneration++;
                _admissionAvailability = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                Job.Status = ProxyJobStatus.Queued;
                return true;
            }
        }

        public TimeSpan NextAdmissionBackoff(
            TimeSpan minimum,
            TimeSpan maximum,
            out bool firstRejection)
        {
            lock (_lock)
            {
                int attempt = ++_consecutiveAdmissionRejections;
                firstRejection = attempt == 1;
                double factor = Math.Pow(2, Math.Min(attempt - 1, 16));
                double milliseconds = Math.Min(
                    maximum.TotalMilliseconds,
                    minimum.TotalMilliseconds * factor);
                return TimeSpan.FromMilliseconds(milliseconds);
            }
        }

        public void ResetAdmissionRejections()
        {
            lock (_lock)
            {
                _consecutiveAdmissionRejections = 0;
            }
        }

        public bool TryResumeAdmission(long generation)
        {
            lock (_lock)
            {
                if (_disposed
                    || _terminalTransitionClaimed
                    || _cancellationRequested
                    || Cancellation.IsCancellationRequested
                    || IsTerminal(Job.Status)
                    || !_admissionDeferred
                    || _admissionGeneration != generation)
                {
                    return false;
                }

                _admissionDeferred = false;
                _admissionAvailability = null;
                return true;
            }
        }

        public bool TryGetAdmissionWait(out long generation, out Task availability)
        {
            lock (_lock)
            {
                if (!_admissionDeferred || _admissionAvailability is null)
                {
                    generation = 0;
                    availability = Task.CompletedTask;
                    return false;
                }

                generation = _admissionGeneration;
                availability = _admissionAvailability.Task;
                return true;
            }
        }

        public void SignalAdmissionAvailability(long generation)
        {
            TaskCompletionSource? availability;
            lock (_lock)
            {
                if (!_admissionDeferred || _admissionGeneration != generation)
                {
                    return;
                }

                availability = _admissionAvailability;
            }

            availability?.TrySetResult();
        }

        public bool IsAdmissionDeferred => _admissionDeferred;

        public bool TryPublishAdmissionWaiting(Func<bool> publish)
        {
            lock (_lock)
            {
                if (_disposed
                    || !_admissionDeferred
                    || _cancellationRequested
                    || Cancellation.IsCancellationRequested
                    || IsTerminal(Job.Status))
                {
                    return false;
                }

            }
            return publish();
        }

        public bool TryCompleteSuccess()
        {
            lock (_lock)
            {
                if (_disposed
                    || _terminalTransitionClaimed
                    || !_cancellationWindowClosed
                    || IsTerminal(Job.Status))
                {
                    return false;
                }

                Job.Status = ProxyJobStatus.Succeeded;
                _terminalTransitionClaimed = true;
                return true;
            }
        }

        public bool TryClaimNonCancellationTerminal(bool cancellationWins)
        {
            lock (_lock)
            {
                if (_disposed
                    || _terminalTransitionClaimed
                    || IsTerminal(Job.Status)
                    || (cancellationWins
                        && (_cancellationRequested
                            || Cancellation.IsCancellationRequested)))
                {
                    return false;
                }

                _terminalTransitionClaimed = true;
                return true;
            }
        }

        public bool TryCloseCancellationWindow()
        {
            lock (_lock)
            {
                if (_disposed
                    || _terminalTransitionClaimed
                    || _cancellationWindowClosed
                    || _cancellationRequested
                    || Cancellation.IsCancellationRequested
                    || IsTerminal(Job.Status))
                {
                    return false;
                }

                _cancellationWindowClosed = true;
                return true;
            }
        }

        public bool TryClaimCancellationDuringCleanup()
        {
            lock (_lock)
            {
                if (_disposed
                    || _terminalTransitionClaimed
                    || _cancellationWindowClosed
                    || !_cancellationRequested
                    && !Cancellation.IsCancellationRequested
                    || IsTerminal(Job.Status))
                {
                    return false;
                }

                _cancellationWindowClosed = true;
                return true;
            }
        }

        public bool TryCancelQueued()
        {
            TaskCompletionSource cancellationCompleted;
            lock (_lock)
            {
                if (_disposed
                    || _started
                    || _terminalTransitionClaimed
                    || _cancellationRequested
                    || _cancellationWindowClosed
                    || IsTerminal(Job.Status))
                    return false;

                _cancellationRequested = true;
                cancellationCompleted = _cancellationCallbacksCompleted = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            CancelSource(cancellationCompleted);

            return true;
        }

        public void Cancel()
        {
            TaskCompletionSource cancellationCompleted;
            lock (_lock)
            {
                if (_disposed
                    || _terminalTransitionClaimed
                    || _cancellationRequested
                    || _cancellationWindowClosed
                    || IsTerminal(Job.Status))
                    return;

                _cancellationRequested = true;
                cancellationCompleted = _cancellationCallbacksCompleted = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            CancelSource(cancellationCompleted);
        }

        public Task WaitForCancellationCallbacksAsync()
        {
            lock (_lock)
            {
                return _cancellationCallbacksCompleted?.Task ?? Task.CompletedTask;
            }
        }

        private void CancelSource(TaskCompletionSource cancellationCompleted)
        {
            try
            {
                Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                cancellationCompleted.TrySetResult();
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                Cancellation.Dispose();
            }
        }
    }
}
