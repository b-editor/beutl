using Beutl.Graphics;

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
    private readonly Func<float, (SKTextBlob? TextBlob, SKPath? StrokePath, Rect RasterBounds)> _factory;

    public ScaledTextCache(Func<float, (SKTextBlob? TextBlob, SKPath? StrokePath, Rect RasterBounds)> factory)
    {
        _factory = factory;
    }

    // Returns borrowed handles owned by the cache; the caller must not dispose them or hold them
    // past a subsequent Get that may evict the entry.
    public (SKTextBlob? TextBlob, SKPath? StrokePath, Rect RasterBounds) Get(float density)
    {
        if (_cache.TryGetValue(density, out Entry? cache))
        {
            _lru.Remove(cache.LruNode);
            _lru.AddFirst(cache.LruNode);
            return (cache.TextBlob, cache.StrokePath, cache.RasterBounds);
        }

        (SKTextBlob? textBlob, SKPath? strokePath, Rect rasterBounds) = _factory(density);

        while (_cache.Count >= MaxEntries && _lru.Last is { } lru)
        {
            _lru.RemoveLast();
            if (_cache.Remove(lru.Value, out Entry? evicted))
            {
                evicted.Dispose();
            }
        }

        cache = new Entry(textBlob, strokePath, rasterBounds, _lru.AddFirst(density));
        _cache.Add(density, cache);
        return (textBlob, strokePath, rasterBounds);
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

    private sealed class Entry : IDisposable
    {
        public Entry(
            SKTextBlob? textBlob,
            SKPath? strokePath,
            Rect rasterBounds,
            LinkedListNode<float> lruNode)
        {
            TextBlob = textBlob;
            StrokePath = strokePath;
            RasterBounds = rasterBounds;
            LruNode = lruNode;
        }

        public SKTextBlob? TextBlob { get; }

        public SKPath? StrokePath { get; }

        public Rect RasterBounds { get; }

        public LinkedListNode<float> LruNode { get; }

        public void Dispose()
        {
            TextBlob?.Dispose();
            StrokePath?.Dispose();
        }
    }
}
