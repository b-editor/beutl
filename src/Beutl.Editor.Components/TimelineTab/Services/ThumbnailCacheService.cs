using System.Diagnostics.CodeAnalysis;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Editor.Components.TimelineTab.Services;

public sealed class ThumbnailCacheService : IThumbnailCacheService
{
    private const long VideoBudgetBytes = 64 * 1024 * 1024;
    private const int WaveformEntryBudget = 32 * 1024;
    private readonly Cache<Ref<Bitmap>> _video;
    private readonly Cache<(float Min, float Max)> _waveforms;

    public static ThumbnailCacheService Instance { get; } = new();

    public ThumbnailCacheService() : this(VideoBudgetBytes, WaveformEntryBudget) { }

    internal ThumbnailCacheService(long videoBudgetBytes, int waveformEntryBudget)
    {
        _video = new(videoBudgetBytes, static bitmap => bitmap.Clone(), static bitmap => bitmap.Dispose());
        _waveforms = new(waveformEntryBudget, static value => value, static _ => { });
    }

    public bool TryGet(string cacheKey, MediaTime time, MediaTime threshold, out Bitmap? bitmap)
    {
        using var lease = _video.TryGet(cacheKey, time.Flicks, threshold.Flicks, out var cached) ? cached : null;
        bitmap = lease?.Value.Clone();
        return bitmap != null;
    }

    public void Save(string cacheKey, MediaTime time, Bitmap bitmap)
    {
        using var lease = Ref<Bitmap>.Create(bitmap.Clone());
        SaveVideo(cacheKey, time, lease);
    }

    public Ref<Bitmap>? AcquireVideo(string cacheKey, MediaTime time)
        => _video.TryGet(cacheKey, time.Flicks, 0, out var bitmap) ? bitmap : null;

    public void SaveVideo(string cacheKey, MediaTime time, Ref<Bitmap> bitmap)
        => _video.Save(cacheKey, time.Flicks, bitmap, bitmap.Value.ByteCount);

    public bool TryGetWaveform(string cacheKey, MediaTime time, MediaTime threshold, out float minValue, out float maxValue)
    {
        bool found = _waveforms.TryGet(cacheKey, time.Flicks, threshold.Flicks, out var peaks);
        (minValue, maxValue) = peaks;
        return found;
    }

    public void SaveWaveform(string cacheKey, MediaTime time, float minValue, float maxValue)
        => _waveforms.Save(cacheKey, time.Flicks, (minValue, maxValue), 1);

    public void Invalidate(string cacheKey)
    {
        _video.Invalidate(cacheKey);
        _waveforms.Invalidate(cacheKey);
    }

    private sealed class Cache<T>(long budget, Func<T, T> retain, Action<T> release)
    {
        private sealed record Entry(string Key, long Time, T Value, long Cost);

        private readonly object _gate = new();
        private readonly Dictionary<string, SortedList<long, LinkedListNode<Entry>>> _indices = new();
        private readonly LinkedList<Entry> _lru = new();
        private long _used;

        public bool TryGet(string key, long time, long threshold, [MaybeNullWhen(false)] out T value)
        {
            lock (_gate)
            {
                value = default;
                if (!_indices.TryGetValue(key, out var index))
                    return false;

                if (!index.TryGetValue(time, out var node) && threshold > 0)
                    node = FindNearest(index, time, threshold);
                if (node == null)
                    return false;

                _lru.Remove(node);
                _lru.AddLast(node);
                value = retain(node.Value.Value);
                return true;
            }
        }

        public void Save(string key, long time, T value, long cost)
        {
            if (cost <= 0 || cost > budget)
                return;

            lock (_gate)
            {
                T retained = retain(value);
                if (!_indices.TryGetValue(key, out var index))
                    _indices.Add(key, index = new());

                if (index.TryGetValue(time, out var previous))
                    ReleaseEntry(previous);

                index[time] = _lru.AddLast(new Entry(key, time, retained, cost));
                _used += cost;
                while (_used > budget && _lru.First is { } oldest)
                {
                    Entry entry = oldest.Value;
                    var evictedIndex = _indices[entry.Key];
                    evictedIndex.Remove(entry.Time);
                    if (evictedIndex.Count == 0)
                        _indices.Remove(entry.Key);
                    ReleaseEntry(oldest);
                }
            }
        }

        public void Invalidate(string key)
        {
            lock (_gate)
            {
                if (_indices.Remove(key, out var index))
                {
                    foreach (var node in index.Values)
                        ReleaseEntry(node);
                }
            }
        }

        private void ReleaseEntry(LinkedListNode<Entry> node)
        {
            _lru.Remove(node);
            _used -= node.Value.Cost;
            release(node.Value.Value);
        }

        private static LinkedListNode<Entry>? FindNearest(
            SortedList<long, LinkedListNode<Entry>> index, long time, long threshold)
        {
            int low = 0, high = index.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (index.Keys[middle] < time)
                    low = middle + 1;
                else
                    high = middle;
            }

            Int128 before = low > 0 ? (Int128)time - index.Keys[low - 1] : Int128.MaxValue;
            Int128 after = low < index.Count ? (Int128)index.Keys[low] - time : Int128.MaxValue;
            if (before <= after && before <= threshold)
                return index.Values[low - 1];
            return after <= threshold ? index.Values[low] : null;
        }
    }
}
