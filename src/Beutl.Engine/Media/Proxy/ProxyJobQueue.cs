using System.Threading.Channels;

using Beutl.Logging;

using Microsoft.Extensions.Logging;

namespace Beutl.Media.Proxy;

public sealed class ProxyJobQueue : IProxyJobQueue
{
    private static readonly ILogger s_logger = Log.CreateLogger("ProxyJobQueue");
    private readonly Func<IProxyGenerator?> _generatorProvider;
    private IProxyGenerator? _generator;
    private IProxyGeneratorAvailability? _generatorAvailability;
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

    public ProxyJobQueue(IProxyGenerator generator, int capacity = 256)
        : this(EagerProvider(generator), store: null, capacity)
    {
    }

    public ProxyJobQueue(IProxyGenerator generator, IProxyStore? store, int capacity = 256)
        : this(EagerProvider(generator), store, capacity, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30))
    {
    }

    internal ProxyJobQueue(
        IProxyGenerator generator,
        IProxyStore? store,
        int capacity,
        TimeSpan minUnavailableBackoff,
        TimeSpan maxUnavailableBackoff)
        : this(EagerProvider(generator), store, capacity, minUnavailableBackoff, maxUnavailableBackoff)
    {
    }

    /// <summary>
    /// Constructs the queue with a generator resolved lazily from <paramref name="generatorProvider"/>
    /// on the first job dispatch. Supports composition roots where the generator is registered after
    /// the queue is constructed (e.g. a proxy generator registered by an extension's <c>Load</c>,
    /// which runs after the app's <c>RegisterServices</c> builds this queue). The provider may return
    /// null to signal "not yet registered"; such jobs are terminal-skipped and the next dispatch
    /// re-probes, so a job queued before registration is not lost to a permanently-empty resolver.
    /// </summary>
    public ProxyJobQueue(Func<IProxyGenerator?> generatorProvider, IProxyStore? store, int capacity = 256)
        : this(generatorProvider, store, capacity, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30))
    {
    }

    internal ProxyJobQueue(
        Func<IProxyGenerator?> generatorProvider,
        IProxyStore? store,
        int capacity,
        TimeSpan minUnavailableBackoff,
        TimeSpan maxUnavailableBackoff)
    {
        ArgumentNullException.ThrowIfNull(generatorProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(minUnavailableBackoff, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxUnavailableBackoff, minUnavailableBackoff);

        _generatorProvider = generatorProvider;
        _store = store;
        _minUnavailableBackoff = minUnavailableBackoff;
        _maxUnavailableBackoff = maxUnavailableBackoff;
        _channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
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

    // Resolves and caches the generator on first dispatch (single-threaded: the drain task is the
    // only caller). The availability subscription is taken out here so an extension-registered
    // generator that loads after the queue is constructed still drives the queue's resume/backoff.
    private IProxyGenerator? ResolveGenerator()
    {
        if (_generator is { } resolved)
            return resolved;

        IProxyGenerator? generator = _generatorProvider();
        if (generator is null)
            return null;

        _generator = generator;
        _generatorAvailability = generator as IProxyGeneratorAvailability;
        if (_generatorAvailability != null)
            _generatorAvailability.AvailabilityChanged += OnGeneratorAvailabilityChanged;
        return generator;
    }

    public int MaxConcurrency => 1;

    public event EventHandler<ProxyJobChangedEventArgs>? JobChanged;

    public ValueTask<ProxyJob> EnqueueAsync(
        ProxyFingerprint source,
        ProxyPreset preset,
        CancellationToken cancellationToken = default)
        => EnqueueAsync(source, preset, priority: 0, cancellationToken);

    public async ValueTask<ProxyJob> EnqueueAsync(
        ProxyFingerprint source,
        ProxyPreset preset,
        int priority,
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
                OnJobChanged(existingJob, ProxyJobChangeKind.Enqueued);
            return existingJob;
        }

        WorkItem item = newItem!;
        try
        {
            await _channel.Writer.WriteAsync(item, cancellationToken);
            OnJobChanged(item.Job, ProxyJobChangeKind.Enqueued);
            return item.Job;
        }
        catch
        {
            Remove(item);
            item.Dispose();
            throw;
        }
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
        if (_generatorAvailability != null)
            _generatorAvailability.AvailabilityChanged -= OnGeneratorAvailabilityChanged;

        try
        {
            await _drainTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _disposeCts.Dispose();
    }

    private async Task DrainAsync()
    {
        // Each channel entry is a permit to drive one job to a terminal state; the job is chosen by
        // priority (not by which entry was read), so a high-priority enqueue can jump a bulk run.
        await foreach (WorkItem _ in _channel.Reader.ReadAllAsync())
        {
            await ProcessOneAsync().ConfigureAwait(false);
        }
    }

    // Drives one channel permit to a terminal state, then loops while a dispatchable item
    // remains. Each iteration either completes the item or requeues it and waits up to
    // _maxUnavailableBackoff for generator availability, so the loop is bounded by
    // ceil(_maxUnavailableBackoff / _minUnavailableBackoff) consecutive unavailable retries
    // before a successful dispatch or an empty _items set ends the call.
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

            item.Job.Status = ProxyJobStatus.Running;
            OnJobChanged(item.Job, ProxyJobChangeKind.Started);

            IProxyGenerator? generator = ResolveGenerator();
            if (generator is null)
            {
                // No generator registered yet (registry empty: build without FFmpeg, or a generator
                // extension that has not finished loading). Skip this job terminally and let the next
                // dispatch re-probe; once a generator registers, later jobs resolve and use it.
                item.Job.Status = ProxyJobStatus.Skipped;
                item.Job.StatusMessage = "Proxy generation is not available in this build.";
                OnJobChanged(item.Job, ProxyJobChangeKind.Skipped);
                Remove(item);
                item.Dispose();
                return;
            }

            bool requeued = false;
            try
            {
                await generator.GenerateAsync(item.Job).ConfigureAwait(false);
                item.Job.Status = ProxyJobStatus.Succeeded;
                Interlocked.Exchange(ref _consecutiveUnavailable, 0);
                OnJobChanged(item.Job, ProxyJobChangeKind.Succeeded);
            }
            catch (ProxyGenerationSkippedException ex)
            {
                item.Job.Status = ProxyJobStatus.Skipped;
                item.Job.StatusMessage = ex.Message;
                OnJobChanged(item.Job, ProxyJobChangeKind.Skipped);
            }
            catch (OperationCanceledException)
            {
                CompleteCanceled(item);
            }
            catch (ProxyGeneratorUnavailableException ex)
            {
                if (_generatorAvailability == null)
                {
                    // With no availability signal the queue can never learn the generator recovered,
                    // so requeuing would occupy the serial queue forever (e.g. a build without FFmpeg).
                    // Treat it as a terminal skip instead.
                    item.Job.Status = ProxyJobStatus.Skipped;
                    item.Job.StatusMessage = ex.Message;
                    OnJobChanged(item.Job, ProxyJobChangeKind.Skipped);
                }
                else
                {
                    // Unavailability is environmental, not the job's fault: keep the job Queued and
                    // re-probe after a bounded backoff, so a transient failure self-recovers and a
                    // genuinely-missing install keeps the job (and its install prompt) alive.
                    requeued = RequeueForRetry(item);
                    if (!requeued)
                        CompleteCanceled(item);
                }
            }
            catch (Exception ex)
            {
                item.Job.Status = ProxyJobStatus.Failed;
                item.Job.Error = ex;
                RegisterFailure(item.Job, ex.Message);
                OnJobChanged(item.Job, ProxyJobChangeKind.Failed);
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
                if (candidate.Job.Status != ProxyJobStatus.Queued
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
        if (!IsTerminal(item.Job.Status))
        {
            item.Job.Status = ProxyJobStatus.Canceled;
            OnJobChanged(item.Job, ProxyJobChangeKind.Canceled);
        }
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

    private void RegisterFailure(ProxyJob job, string? failureReason)
    {
        if (_store == null)
            return;

        try
        {
            if (_store.TryGet(job.Source, job.Preset) is { State: ProxyState.Ready or ProxyState.Stale })
                return;

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
        }
        catch (Exception ex)
        {
            job.BookkeepingError = ex;
            s_logger.LogError(
                ex,
                "Failed to record Failed proxy entry for {Source} ({Preset}).",
                job.Source.AbsolutePath,
                job.Preset);
        }
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
        JobChanged?.Invoke(this, new ProxyJobChangedEventArgs
        {
            Job = job,
            Kind = kind,
        });
    }

    private static bool IsTerminal(ProxyJobStatus status)
        => status is ProxyJobStatus.Succeeded
            or ProxyJobStatus.Failed
            or ProxyJobStatus.Canceled
            or ProxyJobStatus.Skipped;

    private sealed class WorkItem(ProxyJob job, CancellationTokenSource cancellation) : IDisposable
    {
        private readonly Lock _lock = new();
        private bool _started;
        private bool _disposed;

        public ProxyJob Job { get; } = job;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        // Captured up front so a waiter can observe cancellation even after the source is disposed
        // (reading Cancellation.Token after Dispose throws); the token value stays valid.
        public CancellationToken Token { get; } = cancellation.Token;

        public bool TryStart()
        {
            lock (_lock)
            {
                if (_disposed || Cancellation.IsCancellationRequested)
                    return false;

                _started = true;
                return true;
            }
        }

        public bool ResetForRetry()
        {
            lock (_lock)
            {
                if (_disposed || Cancellation.IsCancellationRequested || IsTerminal(Job.Status))
                    return false;

                _started = false;
                Job.Status = ProxyJobStatus.Queued;
                return true;
            }
        }

        public bool TryCancelQueued()
        {
            lock (_lock)
            {
                if (_disposed || _started || IsTerminal(Job.Status))
                    return false;

                Cancellation.Cancel();
                return true;
            }
        }

        public void Cancel()
        {
            lock (_lock)
            {
                if (_disposed || IsTerminal(Job.Status))
                    return;

                Cancellation.Cancel();
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
