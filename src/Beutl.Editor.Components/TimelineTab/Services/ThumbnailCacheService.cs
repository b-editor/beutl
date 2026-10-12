using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.TimelineTab.Services;

public sealed class ThumbnailCacheService : IThumbnailCacheService
{
    private static readonly Lazy<ThumbnailCacheService> s_instance = new(() => new ThumbnailCacheService());

    private readonly ILogger _logger = Log.CreateLogger<ThumbnailCacheService>();
    private readonly ConcurrentDictionary<string, CacheIndex<Bitmap>> _indices = new();
    private readonly ConcurrentDictionary<string, CacheIndex<(float Min, float Max)>> _waveformIndices = new();
    private const int MaxTotalEntries = 512;

    public static ThumbnailCacheService Instance => s_instance.Value;

    public bool TryGet(string cacheKey, TimeSpan time, TimeSpan threshold, out Bitmap? bitmap)
    {
        bitmap = null;

        var index = GetOrCreateIndex(cacheKey);
        long targetTicks = time.Ticks;

        lock (index.Lock)
        {
            if (TryFindNearest(index.Entries, targetTicks, threshold.Ticks, out Bitmap? cached))
            {
                bitmap = cached.Clone();
            }
        }

        if (bitmap == null)
            return false;

        index.LastAccessTime = DateTime.UtcNow;
        return true;
    }

    public void Save(string cacheKey, TimeSpan time, Bitmap bitmap)
    {
        try
        {
            var clone = bitmap.Clone();

            var index = GetOrCreateIndex(cacheKey);
            Bitmap? old = null;
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

    public bool TryGetWaveform(string cacheKey, TimeSpan time, TimeSpan threshold, out float minValue, out float maxValue)
    {
        minValue = 0;
        maxValue = 0;

        var index = GetOrCreateWaveformIndex(cacheKey);
        long targetTicks = time.Ticks;

        lock (index.Lock)
        {
            if (!TryFindNearest(index.Entries, targetTicks, threshold.Ticks, out (float Min, float Max) result))
                return false;

            minValue = result.Min;
            maxValue = result.Max;
        }

        index.LastAccessTime = DateTime.UtcNow;
        return true;
    }

    public void SaveWaveform(string cacheKey, TimeSpan time, float minValue, float maxValue)
    {
        var index = GetOrCreateWaveformIndex(cacheKey);
        lock (index.Lock)
        {
            index.Entries[time.Ticks] = (minValue, maxValue);
        }

        index.LastAccessTime = DateTime.UtcNow;

        TryEvictCache();
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

        if (_waveformIndices.TryRemove(cacheKey, out var waveformIndex))
        {
            lock (waveformIndex.Lock)
            {
                waveformIndex.Entries.Clear();
            }
        }
    }

    private CacheIndex<Bitmap> GetOrCreateIndex(string cacheKey)
    {
        return _indices.GetOrAdd(cacheKey, _ => new CacheIndex<Bitmap>());
    }

    private CacheIndex<(float Min, float Max)> GetOrCreateWaveformIndex(string cacheKey)
    {
        return _waveformIndices.GetOrAdd(cacheKey, _ => new CacheIndex<(float Min, float Max)>());
    }

    private static bool TryFindNearest<T>(
        SortedList<long, T> entries, long targetTicks, long thresholdTicks, [MaybeNullWhen(false)] out T value)
    {
        value = default;
        if (entries.Count == 0)
            return false;

        var keys = entries.Keys;

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
        int best = -1;

        for (int i = Math.Max(0, lo - 1); i <= Math.Min(keys.Count - 1, lo); i++)
        {
            long diff = Math.Abs(keys[i] - targetTicks);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = i;
            }
        }

        if (best < 0 || bestDiff > thresholdTicks)
            return false;

        value = entries[keys[best]];
        return true;
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

        foreach (var kvp in _waveformIndices)
        {
            lock (kvp.Value.Lock)
            {
                totalEntries += kvp.Value.Entries.Count;
            }
        }

        if (totalEntries <= MaxTotalEntries)
            return;

        // LRU: bitmap と waveform 両方のアクセス時間で古い順にソートして削除
        var sortedBitmaps = _indices.Select(kvp => (Key: kvp.Key, Time: kvp.Value.LastAccessTime, IsBitmap: true)).ToList();
        var sortedWaveforms = _waveformIndices.Select(kvp => (Key: kvp.Key, Time: kvp.Value.LastAccessTime, IsBitmap: false)).ToList();
        var sorted = sortedBitmaps.Concat(sortedWaveforms).OrderBy(x => x.Time).ToList();

        foreach (var item in sorted)
        {
            if (totalEntries <= MaxTotalEntries)
                break;

            int? removed = item.IsBitmap ? Evict(_indices, item.Key) : Evict(_waveformIndices, item.Key);
            if (removed is not { } count)
                continue;

            totalEntries -= count;
            if (item.IsBitmap)
                _logger.LogDebug("Evicted thumbnail cache: {Key}", item.Key);
            else
                _logger.LogDebug("Evicted waveform cache: {Key}", item.Key);
        }
    }

    // Removes the index of key and returns how many entries it held, disposing bitmaps; null when it is gone.
    private static int? Evict<T>(ConcurrentDictionary<string, CacheIndex<T>> indices, string key)
    {
        if (!indices.TryRemove(key, out var index))
            return null;

        lock (index.Lock)
        {
            int removed = index.Entries.Count;
            foreach (var entry in index.Entries.Values)
            {
                (entry as IDisposable)?.Dispose();
            }

            index.Entries.Clear();
            return removed;
        }
    }

    private sealed class CacheIndex<T>
    {
        public readonly SortedList<long, T> Entries = new();
        public readonly object Lock = new();
        public DateTime LastAccessTime = DateTime.UtcNow;
    }
}
