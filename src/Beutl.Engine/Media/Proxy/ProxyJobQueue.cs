using System.Threading.Channels;

namespace Beutl.Media.Proxy;

public sealed class ProxyJobQueue : IProxyJobQueue
{
    private readonly IProxyGenerator _generator;
    private readonly IProxyStore? _store;
    private readonly Channel<WorkItem> _channel;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly TaskCompletionSource _resumeAfterGeneratorUnavailable = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<(ProxyFingerprint Source, ProxyPreset Preset), WorkItem> _itemsByKey = [];
    private readonly List<WorkItem> _items = [];
    private readonly Lock _lock = new();
    private readonly Task _drainTask;
    private bool _disposed;

    public ProxyJobQueue(IProxyGenerator generator, int capacity = 256)
        : this(generator, store: null, capacity)
    {
    }

    public ProxyJobQueue(IProxyGenerator generator, IProxyStore? store, int capacity = 256)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _generator = generator;
        _store = store;
        _channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        _drainTask = Task.Run(DrainAsync);
    }

    public int MaxConcurrency => 1;

    public event EventHandler<ProxyJobChangedEventArgs>? JobChanged;

    public async ValueTask<ProxyJob> EnqueueAsync(
        ProxyFingerprint source,
        ProxyPreset preset,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var key = (source, preset);
        WorkItem item;
        lock (_lock)
        {
            if (_itemsByKey.TryGetValue(key, out WorkItem? existing)
                && !IsTerminal(existing.Job.Status))
            {
                return existing.Job;
            }

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
                cts.Token);

            item = new WorkItem(job, cts);
            _itemsByKey.Add(key, item);
            _items.Add(item);
        }

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
        await foreach (WorkItem item in _channel.Reader.ReadAllAsync())
        {
            bool removedBeforeFinally = false;
            try
            {
                if (item.IsCancellationRequested)
                {
                    CompleteCanceled(item);
                    continue;
                }

                item.Job.Status = ProxyJobStatus.Running;
                OnJobChanged(item.Job, ProxyJobChangeKind.Started);

                await _generator.GenerateAsync(item.Job).ConfigureAwait(false);
                item.Job.Status = ProxyJobStatus.Succeeded;
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
                item.Job.Status = ProxyJobStatus.Failed;
                item.Job.Error = ex;
                item.Job.StatusMessage = ex.Message;
                RegisterFailure(item.Job, ex.Message);
                OnJobChanged(item.Job, ProxyJobChangeKind.Failed);
                Remove(item);
                item.Dispose();
                removedBeforeFinally = true;
                await WaitForGeneratorResumeOrDisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                item.Job.Status = ProxyJobStatus.Failed;
                item.Job.Error = ex;
                RegisterFailure(item.Job, ex.Message);
                OnJobChanged(item.Job, ProxyJobChangeKind.Failed);
            }
            finally
            {
                if (!removedBeforeFinally)
                {
                    Remove(item);
                    item.Dispose();
                }
            }
        }
    }

    private async Task WaitForGeneratorResumeOrDisposeAsync()
    {
        try
        {
            await _resumeAfterGeneratorUnavailable.Task.WaitAsync(_disposeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
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
        if (item.Job.Status == ProxyJobStatus.Queued)
        {
            CompleteCanceled(item);
            Remove(item);
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
        catch
        {
        }
    }

    private void Remove(WorkItem item)
    {
        lock (_lock)
        {
            _itemsByKey.Remove((item.Job.Source, item.Job.Preset));
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
        private bool _disposed;

        public ProxyJob Job { get; } = job;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public bool IsCancellationRequested
        {
            get
            {
                lock (_lock)
                {
                    return _disposed || Cancellation.IsCancellationRequested;
                }
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
