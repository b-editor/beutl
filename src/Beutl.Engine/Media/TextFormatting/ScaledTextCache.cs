using SkiaSharp;

namespace Beutl.Media.TextFormatting;

// Caps the per-density blob/stroke cache so varying densities (e.g. window-resize scaling) can't
// grow it without bound; the least-recently-used density is evicted. Handles are produced by the
// injected factory and owned by the cache.
internal sealed class ScaledTextCache : IDisposable
{
    private const int MaxEntries = 8;
    private readonly Dictionary<float, Entry> _cache = [];
    private readonly LinkedList<float> _lru = new();
    private readonly Func<float, (SKTextBlob? TextBlob, SKPath? StrokePath)> _factory;

    // Test seam (Beutl.UnitTests): fires after the density-scaled blob/stroke are produced and the
    // LRU node is added, but before the entry is committed to _cache, so the leak-cleanup and
    // LRU-rollback path can be driven deterministically. Null in production.
    internal Action<SKTextBlob?, SKPath?>? CommitFaultHook;

    public ScaledTextCache(Func<float, (SKTextBlob? TextBlob, SKPath? StrokePath)> factory)
    {
        _factory = factory;
    }

    // Test seam (Beutl.UnitTests): the LRU list and the cache dictionary must stay in lockstep; a
    // leaked LRU node (e.g. a failed commit that doesn't roll back) breaks this invariant.
    internal (int CacheCount, int LruCount) Counts => (_cache.Count, _lru.Count);

    // Returns borrowed handles owned by the cache; the caller must not dispose them or hold them
    // past a subsequent Get that may evict the entry.
    public (SKTextBlob? TextBlob, SKPath? StrokePath) Get(float density)
    {
        if (_cache.TryGetValue(density, out Entry? cache))
        {
            _lru.Remove(cache.LruNode);
            _lru.AddFirst(cache.LruNode);
            return (cache.TextBlob, cache.StrokePath);
        }

        (SKTextBlob? textBlob, SKPath? strokePath) = _factory(density);

        // Nothing owns textBlob/strokePath until _cache.Add succeeds, so a throw from eviction
        // disposal or either cache mutation in between would leak the native handles. Dispose them
        // and roll back the LRU node on failure to keep the two collections consistent.
        LinkedListNode<float>? node = null;
        try
        {
            while (_cache.Count >= MaxEntries && _lru.Last is { } lru)
            {
                _lru.RemoveLast();
                if (_cache.Remove(lru.Value, out Entry? evicted))
                {
                    evicted.Dispose();
                }
            }

            node = _lru.AddFirst(density);
            CommitFaultHook?.Invoke(textBlob, strokePath);
            cache = new Entry(textBlob, strokePath, node);
            _cache.Add(density, cache);
            return (textBlob, strokePath);
        }
        catch
        {
            if (node is not null)
            {
                _lru.Remove(node);
            }

            // Best-effort: a faulting handle Dispose must not mask the original commit failure,
            // and the second handle must still be released if the first throws.
            DisposeBestEffort(textBlob);
            DisposeBestEffort(strokePath);
            throw;
        }
    }

    // Leaves the cache empty but reusable: callers re-measure on a property change and Get
    // repopulates, so do not add a disposed gate.
    public void Clear()
    {
        foreach (Entry item in _cache.Values)
        {
            item.Dispose();
        }

        _cache.Clear();
        _lru.Clear();
    }

    // No finalizer: every owned handle (SKTextBlob / SKPath) is itself finalizable via SkiaSharp, so
    // deterministic Dispose only speeds up release.
    public void Dispose() => Clear();

    private static void DisposeBestEffort(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch
        {
            // Preserve the original commit failure; the leak this guards against is the larger risk.
        }
    }

    private sealed class Entry : IDisposable
    {
        public Entry(SKTextBlob? textBlob, SKPath? strokePath, LinkedListNode<float> lruNode)
        {
            TextBlob = textBlob;
            StrokePath = strokePath;
            LruNode = lruNode;
        }

        public SKTextBlob? TextBlob { get; }

        public SKPath? StrokePath { get; }

        public LinkedListNode<float> LruNode { get; }

        public void Dispose()
        {
            TextBlob?.Dispose();
            StrokePath?.Dispose();
        }
    }
}
