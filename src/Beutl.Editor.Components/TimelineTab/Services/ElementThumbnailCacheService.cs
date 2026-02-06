using System.Collections.Concurrent;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Operation;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.TimelineTab.Services;

public sealed class ElementThumbnailCacheService : IElementThumbnailCacheService
{
    private static readonly Lazy<ElementThumbnailCacheService> s_instance = new(() => new ElementThumbnailCacheService());

    private readonly ILogger _logger = Log.CreateLogger<ElementThumbnailCacheService>();
    private readonly ConcurrentDictionary<string, CacheIndex> _indices = new();
    private const int MaxTotalEntries = 512;

    public static ElementThumbnailCacheService Instance => s_instance.Value;

    public bool TryGet(string cacheKey, TimeSpan time, TimeSpan threshold, out IBitmap? bitmap)
    {
        bitmap = null;

        var index = GetOrCreateIndex(cacheKey);
        long targetTicks = time.Ticks;

        lock (index.Lock)
        {
            var cached = FindNearest(index.Entries, targetTicks, threshold.Ticks);
            if (cached != null)
            {
                try
                {
                    bitmap = cached.Clone();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clone cached thumbnail");
                    return false;
                }
            }
        }

        if (bitmap == null)
            return false;

        index.LastAccessTime = DateTime.UtcNow;
        return true;
    }

    public void Save(string cacheKey, TimeSpan time, IBitmap bitmap)
    {
        try
        {
            var clone = bitmap.Clone();

            var index = GetOrCreateIndex(cacheKey);
            IBitmap? old = null;
            lock (index.Lock)
            {
                if (index.Entries.TryGetValue(time.Ticks, out var existing))
                {
                    old = existing;
                }

                index.Entries[time.Ticks] = clone;
            }

            old?.Dispose();
            index.LastAccessTime = DateTime.UtcNow;

            TryEvictCache();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save thumbnail cache: {CacheKey} at {Time}", cacheKey, time);
        }
    }

    public void Invalidate(string cacheKey)
    {
        if (_indices.TryRemove(cacheKey, out var index))
        {
            lock (index.Lock)
            {
                foreach (var entry in index.Entries.Values)
                {
                    entry.Dispose();
                }

                index.Entries.Clear();
            }
        }
    }

    private CacheIndex GetOrCreateIndex(string cacheKey)
    {
        return _indices.GetOrAdd(cacheKey, _ => new CacheIndex());
    }

    private static IBitmap? FindNearest(SortedList<long, IBitmap> entries, long targetTicks, long thresholdTicks)
    {
        if (entries.Count == 0)
            return null;

        var keys = entries.Keys;

        // バイナリサーチ
        int lo = 0, hi = keys.Count - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (keys[mid] < targetTicks)
                lo = mid + 1;
            else
                hi = mid - 1;
        }

        // lo は targetTicks 以上の最小インデックス
        long bestDiff = long.MaxValue;
        IBitmap? best = null;

        // lo-1 (直前の要素) と lo (直後の要素) を比較
        for (int i = Math.Max(0, lo - 1); i <= Math.Min(keys.Count - 1, lo); i++)
        {
            long diff = Math.Abs(keys[i] - targetTicks);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = entries[keys[i]];
            }
        }

        return bestDiff <= thresholdTicks ? best : null;
    }

    private void TryEvictCache()
    {
        int totalEntries = 0;
        foreach (var kvp in _indices)
        {
            lock (kvp.Value.Lock)
            {
                totalEntries += kvp.Value.Entries.Count;
            }
        }

        if (totalEntries <= MaxTotalEntries)
            return;

        // LRU: アクセス時間が古い順にソートして削除
        var sorted = _indices.OrderBy(kvp => kvp.Value.LastAccessTime).ToList();

        foreach (var kvp in sorted)
        {
            if (totalEntries <= MaxTotalEntries)
                break;

            if (_indices.TryRemove(kvp.Key, out var index))
            {
                int removed;
                lock (index.Lock)
                {
                    removed = index.Entries.Count;
                    foreach (var entry in index.Entries.Values)
                    {
                        entry.Dispose();
                    }

                    index.Entries.Clear();
                }

                totalEntries -= removed;
                _logger.LogDebug("Evicted thumbnail cache: {Key}", kvp.Key);
            }
        }
    }

    private sealed class CacheIndex
    {
        public readonly SortedList<long, IBitmap> Entries = new();
        public readonly object Lock = new();
        public DateTime LastAccessTime = DateTime.UtcNow;
    }
}
