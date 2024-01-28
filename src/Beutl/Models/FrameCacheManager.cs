using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using Beutl.Configuration;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;

namespace Beutl.Models;

// not thread safe
public sealed class FrameCacheManager : IDisposable
{
    private readonly SortedDictionary<int, CacheEntry> _entries = [];
    private readonly object _lock = new();
    private readonly PixelSize _frameSize;
    private readonly ulong _maxSize;
    private ulong _size;

    public event Action<int>? Added;
    public event Action<int[]>? Removed;
    public event Action<ImmutableArray<CacheBlock>>? BlocksUpdated;

    public FrameCacheManager(PixelSize frameSize)
    {
        _frameSize = frameSize;
        _maxSize = (ulong)(GlobalConfiguration.Instance.EditorConfig.FrameCacheMaxSize * 1024 * 1024);
    }

    public ImmutableArray<CacheBlock> Blocks { get; private set; }

    public void Add(int frame, Ref<Bitmap<Bgra8888>> bitmap)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(frame, out CacheEntry? old))
            {
                if (!old.IsLocked)
                    old.SetBitmap(bitmap);
            }
            else
            {
                _size += (uint)bitmap.Value.ByteCount;
                _entries.Add(frame, new CacheEntry(bitmap));
                Added?.Invoke(frame);
            }
        }

        if (_size >= _maxSize)
        {
            Task.Run(Optimize);
        }
    }

    public bool TryGet(int frame, [MaybeNullWhen(false)] out Ref<Bitmap<Bgra8888>> bitmap)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(frame, out CacheEntry? e))
            {
                bitmap = e.GetBitmap();
                return true;
            }
            else
            {
                bitmap = null;
                return false;
            }
        }
    }

    public bool RemoveRange(int start, int end)
    {
        lock (_lock)
        {
            int[] keys = _entries.Where(v => !v.Value.IsLocked)
                .Select(p => p.Key)
                .SkipWhile(t => t < start)
                .TakeWhile(t => t < end)
                .ToArray();

            foreach (int key in keys)
            {
                if (_entries.Remove(key, out CacheEntry? e))
                {
                    _size -= (uint)e.ByteCount;
                    e.Dispose();
                }
            }

            if (keys.Length > 0)
                Removed?.Invoke(keys);

            return keys.Length > 0;
        }
    }

    public void Lock(int start, int end)
    {
        lock (_lock)
        {
            foreach (KeyValuePair<int, CacheEntry> item in _entries
                .SkipWhile(t => t.Key < start)
                .TakeWhile(t => t.Key < end))
            {
                if (!item.Value.IsLocked)
                {
                    _size -= (uint)item.Value.ByteCount;
                }

                item.Value.IsLocked = true;
            }
        }
    }

    public void Unlock(int start, int end)
    {
        lock (_lock)
        {
            foreach (KeyValuePair<int, CacheEntry> item in _entries
                .SkipWhile(t => t.Key < start)
                .TakeWhile(t => t.Key < end))
            {
                if (item.Value.IsLocked)
                {
                    _size += (uint)item.Value.ByteCount;
                }

                item.Value.IsLocked = false;
            }
        }
    }

    public void RemoveAndUpdateBlocks(IEnumerable<(int Start, int End)> timeRanges)
    {
        lock (_lock)
        {
            bool removedAnyCache = false;

            foreach ((int Start, int End) in timeRanges)
            {
                removedAnyCache |= RemoveRange(Start, End);
            }

            if (removedAnyCache)
            {
                UpdateBlocks();
            }
        }
    }

    public void Dispose()
    {
        Clear();
    }

    public void Clear()
    {
        lock (_lock)
        {
            int[] keys = [.. _entries.Keys];
            foreach (CacheEntry item in _entries.Values)
            {
                item.Dispose();
            }

            _size = 0;
            _entries.Clear();
            Removed?.Invoke(keys);
            BlocksUpdated?.Invoke([]);
        }
    }

    // |oxxxxoooxoxxxo|
    // GetBlock() -> [(1, 4), (8, 1), (10, 4)]
    public ImmutableArray<CacheBlock> CalculateBlocks()
    {
        lock (_lock)
        {
            var list = new List<CacheBlock>();
            int start = -1;
            int expect = 0;
            int count = 0;
            bool isLocked = false;
            foreach ((int key, CacheEntry item) in _entries)
            {
                if (start == -1)
                {
                    start = key;
                    isLocked = item.IsLocked;
                    expect = key;
                }

                if (expect == key && isLocked == item.IsLocked)
                {
                    count++;
                    expect = key + 1;
                }
                else
                {
                    list.Add(new(start, count, isLocked));
                    start = -1;
                    count = 0;

                    start = key;
                    isLocked = item.IsLocked;
                    expect = key + 1;
                    count++;
                }
            }

            if (start != -1)
            {
                list.Add(new(start, count, isLocked));
            }

            return [.. list];
        }
    }

    public void UpdateBlocks()
    {
        lock (_lock)
        {
            Blocks = CalculateBlocks();
            BlocksUpdated?.Invoke(Blocks);
        }
    }

    private void Optimize()
    {
        lock (_lock)
        {
            if (_size >= _maxSize)
            {
                ulong excess = _size - _maxSize;
                int sizePerCache = _frameSize.Width * _frameSize.Height * 4;
                var targetCount = excess / (ulong)sizePerCache;

                var items = _entries
                    .Where(v => !v.Value.IsLocked)
                    .OrderBy(v => v.Value.LastAccessTime)
                    .Take((int)targetCount)
                    .ToArray();
                foreach (KeyValuePair<int, CacheEntry> item in items)
                {
                    if (_size < _maxSize)
                        break;

                    item.Value.Dispose();
                    _size -= (uint)item.Value.ByteCount;
                    _entries.Remove(item.Key);
                }

                Removed?.Invoke(items.Select(i => i.Key).ToArray());
            }
        }
    }

    public record CacheBlock(int Start, int Length, bool IsLocked);

    private class CacheEntry : IDisposable
    {
        private Ref<Bitmap<Bgra8888>> _bitmap;

        public CacheEntry(Ref<Bitmap<Bgra8888>> bitmap)
        {
            _bitmap = bitmap.Clone();
            ByteCount = bitmap.Value.ByteCount;
            LastAccessTime = DateTime.UtcNow;
        }

        public DateTime LastAccessTime { get; private set; }

        public int ByteCount { get; }

        public bool IsLocked { get; set; }

        public void SetBitmap(Ref<Bitmap<Bgra8888>> bitmap)
        {
            _bitmap?.Dispose();
            _bitmap = bitmap.Clone();
            LastAccessTime = DateTime.UtcNow;
        }

        public Ref<Bitmap<Bgra8888>> GetBitmap()
        {
            LastAccessTime = DateTime.UtcNow;
            return _bitmap.Clone();
        }

        public void Dispose()
        {
            _bitmap?.Dispose();
            _bitmap = null!;
        }
    }

    private sealed class KeyValuePairComparer<TKey, TValue>(IComparer<TKey>? keyComparer) : Comparer<KeyValuePair<TKey, TValue>>
    {
        private readonly IComparer<TKey> _keyComparer = keyComparer ?? Comparer<TKey>.Default;

        public override int Compare(KeyValuePair<TKey, TValue> x, KeyValuePair<TKey, TValue> y)
        {
            return _keyComparer.Compare(x.Key, y.Key);
        }

        public override bool Equals(object? obj)
        {
            if (obj is KeyValuePairComparer<TKey, TValue> other)
            {
                return _keyComparer == other._keyComparer || _keyComparer.Equals(other._keyComparer);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return _keyComparer.GetHashCode();
        }
    }
}
