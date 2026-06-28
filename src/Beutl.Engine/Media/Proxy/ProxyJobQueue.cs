using System.Threading.Channels;

namespace Beutl.Media.Proxy;

public sealed class ProxyJobQueue : IProxyJobQueue
{
    private readonly IProxyGenerator _generator;
    private readonly Channel<WorkItem> _channel;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Dictionary<(ProxyFingerprint Source, ProxyPreset Preset), WorkItem> _itemsByKey = [];
    private readonly List<WorkItem> _items = [];
    private readonly Lock _lock = new();
    private readonly Task _drainTask;
    private bool _disposed;

    public ProxyJobQueue(IProxyGenerator generator, int capacity = 256)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _generator = generator;
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

        item?.Cancel();
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
            item.Cancel();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
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
                OnJobChanged(item.Job, ProxyJobChangeKind.Failed);
            }
            catch (Exception ex)
            {
                item.Job.Status = ProxyJobStatus.Failed;
                item.Job.Error = ex;
                OnJobChanged(item.Job, ProxyJobChangeKind.Failed);
            }
            finally
            {
                Remove(item);
                item.Dispose();
            }
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
