using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

using Beutl.Configuration;
using Beutl.Graphics;
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
    public event Action<ImmutableArray<(int Start, int Length)>>? BlocksUpdated;

    public FrameCacheManager(PixelSize frameSize)
    {
        _frameSize = frameSize;
        _maxSize = (ulong)(GlobalConfiguration.Instance.EditorConfig.FrameCacheMaxSize * 1024 * 1024);
    }

    public ImmutableArray<(int Start, int Length)> Blocks { get; private set; }

    public void Add(int frame, Ref<Bitmap<Bgra8888>> bitmap)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(frame, out CacheEntry? old))
            {
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
            int[] keys = _entries.Select(p => p.Key)
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

    public void Dispose()
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
        }
    }

    // |oxxxxoooxoxxxo|
    // GetBlock() -> [(1, 4), (8, 1), (10, 4)]
    public ImmutableArray<(int Start, int Length)> CalculateBlocks()
    {
        lock (_lock)
        {
            var list = new List<(int Start, int Length)>();
            int start = -1;
            int expect = 0;
            int count = 0;
            foreach (int key in _entries.Keys)
            {
                if (start == -1)
                {
                    start = key;
                    expect = key;
                }

                if (expect == key)
                {
                    count++;
                    expect = key + 1;
                }
                else
                {
                    list.Add((start, count));
                    start = -1;
                    count = 0;

                    start = key;
                    expect = key + 1;
                    count++;
                }
            }

            if (start != -1)
            {
                list.Add((start, count));
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

    private class CacheEntry : IDisposable
    {
        private readonly int _width;
        private readonly int _height;
        private Ref<Bitmap<Bgra8888>> _bitmap;

        public CacheEntry(Ref<Bitmap<Bgra8888>> bitmap)
        {
            _bitmap = bitmap.Clone();
            _width = _bitmap.Value.Width;
            _height = _bitmap.Value.Height;
            ByteCount = bitmap.Value.ByteCount;
            LastAccessTime = DateTime.UtcNow;
        }

        public DateTime LastAccessTime { get; private set; }

        public int ByteCount { get; }

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
