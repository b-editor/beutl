using Beutl.Media.Proxy;

namespace Beutl.Services;

// Stable facades over the live proxy services. A ViewModel caches the service it gets from
// IEditorContext once and subscribes to its events; when ProxyMediaServices rebuilds the backing
// store/queue/resolver (a store-root or cap change), the facade instance stays the same and forwards
// calls plus events to the new backing service, so the open editor keeps talking to the current store.
internal sealed class StableProxyStoreFacade(IProxyStore initial) : IProxyStore
{
    private IProxyStore _inner = initial;
    private EventHandler<ProxyStoreChangedEventArgs>? _changed;

    public void Swap(IProxyStore next)
    {
        IProxyStore previous = _inner;
        if (ReferenceEquals(previous, next))
            return;

        EventHandler<ProxyStoreChangedEventArgs>? handlers = _changed;
        if (handlers is not null)
            previous.Changed -= handlers;

        _inner = next;
        if (handlers is not null)
            next.Changed += handlers;
    }

    public string StoreRootPath => _inner.StoreRootPath;

    public ProxyEntry? TryGet(ProxyFingerprint source, ProxyPreset preset) => _inner.TryGet(source, preset);

    public IReadOnlyList<ProxyEntry> Enumerate() => _inner.Enumerate();

    public void Register(ProxyEntry entry) => _inner.Register(entry);

    public bool TryTransition(ProxyFingerprint source, ProxyPreset preset, ProxyState newState, string? failureReason = null)
        => _inner.TryTransition(source, preset, newState, failureReason);

    public bool Delete(ProxyFingerprint source, ProxyPreset preset) => _inner.Delete(source, preset);

    public void Touch(ProxyFingerprint source, ProxyPreset preset, DateTime nowUtc) => _inner.Touch(source, preset, nowUtc);

    public long GetTotalBytes() => _inner.GetTotalBytes();

    public long GetTotalBytes(IReadOnlySet<string> sourceAbsolutePaths) => _inner.GetTotalBytes(sourceAbsolutePaths);

    public Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public Task ReconcileAsync(CancellationToken cancellationToken) => _inner.ReconcileAsync(cancellationToken);

    public event EventHandler<ProxyStoreChangedEventArgs>? Changed
    {
        add
        {
            _changed += value;
            _inner.Changed += value;
        }
        remove
        {
            _changed -= value;
            _inner.Changed -= value;
        }
    }
}

internal sealed class StableProxyQueueFacade(IProxyJobQueue initial) : IProxyJobQueue
{
    private IProxyJobQueue _inner = initial;
    private EventHandler<ProxyJobChangedEventArgs>? _jobChanged;

    public void Swap(IProxyJobQueue next)
    {
        IProxyJobQueue previous = _inner;
        if (ReferenceEquals(previous, next))
            return;

        EventHandler<ProxyJobChangedEventArgs>? handlers = _jobChanged;
        if (handlers is not null)
            previous.JobChanged -= handlers;

        _inner = next;
        if (handlers is not null)
            next.JobChanged += handlers;
    }

    public int MaxConcurrency => _inner.MaxConcurrency;

    public ValueTask<ProxyJob> EnqueueAsync(ProxyFingerprint source, ProxyPreset preset, int priority = 0, CancellationToken cancellationToken = default)
        => _inner.EnqueueAsync(source, preset, priority, cancellationToken);

    public IReadOnlyList<ProxyJob> Pending() => _inner.Pending();

    public void Cancel(Guid jobId) => _inner.Cancel(jobId);

    public void CancelAll() => _inner.CancelAll();

    // The facade never owns the queue lifetime; ProxyMediaServices disposes the real queues.
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public event EventHandler<ProxyJobChangedEventArgs>? JobChanged
    {
        add
        {
            _jobChanged += value;
            _inner.JobChanged += value;
        }
        remove
        {
            _jobChanged -= value;
            _inner.JobChanged -= value;
        }
    }
}

internal sealed class StableProxyResolverFacade(IProxyResolver initial) : IProxyResolver
{
    private IProxyResolver _inner = initial;

    public void Swap(IProxyResolver next) => _inner = next;

    public ProxyResolution? Resolve(Uri sourceUri, ProxyPreset preferredPreset) => _inner.Resolve(sourceUri, preferredPreset);

    public long GetSourceVersion(string absolutePath) => _inner.GetSourceVersion(absolutePath);

    public IDisposable Pin(ProxyResolution resolution) => _inner.Pin(resolution);

    public bool IsPinned(string absoluteProxyFilePath) => _inner.IsPinned(absoluteProxyFilePath);
}

internal sealed class StableProxyCapInfoFacade(IProxyStoreCapInfo initial) : IProxyStoreCapInfo
{
    private IProxyStoreCapInfo _inner = initial;

    public void Swap(IProxyStoreCapInfo next) => _inner = next;

    public long MaxTotalBytes => _inner.MaxTotalBytes;
}
