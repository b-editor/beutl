using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using Beutl.Configuration;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;

using Reactive.Bindings;

namespace Beutl.Models;

public sealed partial class FrameCacheManager : IDisposable
{
    private readonly SortedDictionary<int, CacheEntry> _entries = [];
    private readonly object _lock = new();
    private readonly ReadOnlyReactivePropertySlim<long> _maxSize;
    private long _size;

    public event Action<ImmutableArray<CacheBlock>>? BlocksUpdated;

    public FrameCacheManager(PixelSize frameSize, FrameCacheOptions options)
    {
        FrameSize = frameSize;
        Options = options;

        _maxSize = GlobalConfiguration.Instance.EditorConfig.GetObservable(EditorConfig.FrameCacheMaxSizeProperty)
            .Select(v => (long)(v * 1024 * 1024))
            .ToReadOnlyReactivePropertySlim();
    }

    public ImmutableArray<CacheBlock> Blocks { get; private set; }

    public FrameCacheOptions Options { get; set; }

    // 再生中のフレーム
    public int CurrentFrame { get; set; }

    public bool IsEnabled { get; set; }

    public PixelSize FrameSize { get; }

    public void Add(int frame, Ref<Bitmap<Bgra8888>> bitmap)
    {
        if (!IsEnabled) return;

        lock (_lock)
        {
            if (_entries.TryGetValue(frame, out CacheEntry? old))
            {
                if (!old.IsLocked)
                    old.SetBitmap(bitmap, Options);
            }
            else
            {
                var entry = new CacheEntry(bitmap, Options);
                _size += entry.ByteCount;
                _entries.Add(frame, entry);
            }
        }

        if (_size >= _maxSize.Value)
        {
            Task.Run(AutoDelete);
        }
    }

    public bool TryGet(int frame, [MaybeNullWhen(false)] out Ref<Bitmap<Bgra8888>> bitmap)
    {
        if (!IsEnabled)
        {
            bitmap = null;
            return false;
        }

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

    public bool DeleteRange(int start, int end)
    {
        lock (_lock)
        {
            KeyValuePair<int, CacheEntry>[] items
                = GetRange(_entries.Where(v => !v.Value.IsLocked), start, end).ToArray();

            foreach ((int key, CacheEntry e) in items)
            {
                _entries.Remove(key);
                _size -= e.ByteCount;
                e.Dispose();
            }

            return items.Length > 0;
        }
    }

    public void Lock(int start, int end)
    {
        lock (_lock)
        {
            foreach (KeyValuePair<int, CacheEntry> item in GetRange( _entries, start, end))
            {
                if (!item.Value.IsLocked)
                {
                    _size -= item.Value.ByteCount;
                }

                item.Value.IsLocked = true;
            }
        }
    }

    public void Unlock(int start, int end)
    {
        lock (_lock)
        {
            foreach (KeyValuePair<int, CacheEntry> item in GetRange( _entries, start, end))
            {
                if (item.Value.IsLocked)
                {
                    _size += item.Value.ByteCount;
                }

                item.Value.IsLocked = false;
            }
        }
    }

    public long CalculateByteCount(int start, int end)
    {
        lock (_lock)
        {
            return GetRange(_entries, start, end).Sum(t => (long)t.Value.ByteCount);
        }
    }

    public void DeleteAndUpdateBlocks(IEnumerable<(int Start, int End)> timeRanges)
    {
        lock (_lock)
        {
            bool removedAnyCache = false;

            foreach ((int Start, int End) in timeRanges)
            {
                removedAnyCache |= DeleteRange(Start, End);
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
        _maxSize.Dispose();
    }

    public void Clear()
    {
        lock (_lock)
        {
            foreach (CacheEntry item in _entries.Values)
            {
                item.Dispose();
            }

            _size = 0;
            _entries.Clear();
            BlocksUpdated?.Invoke([]);
        }
    }

    private void AutoDelete()
    {
        int currentFrame = CurrentFrame;
        KeyValuePair<int, CacheEntry>[] GetOldCaches(long targetCount)
        {
            return _entries
                .Where(v => !v.Value.IsLocked)
                .OrderBy(v => v.Value.LastAccessTime)
                .Take((int)targetCount)
                .ToArray();
        }

        KeyValuePair<int, CacheEntry>[] GetFarCaches(long targetCount)
        {
            return _entries
                .Where(v => !v.Value.IsLocked && v.Key < currentFrame)
                .OrderBy(v => v.Key - currentFrame)
                .Take((int)targetCount)
                .ToArray();
        }

        void DeleteItems(KeyValuePair<int, CacheEntry>[] items)
        {
            foreach (KeyValuePair<int, CacheEntry> item in items)
            {
                if (_size < _maxSize.Value)
                    break;

                _size -= item.Value.ByteCount;
                item.Value.Dispose();
                _entries.Remove(item.Key);
            }
        }

        void DeleteBackwardBlock()
        {
            ImmutableArray<CacheBlock> blocks = CalculateBlocks(int.MinValue, currentFrame);
            CacheBlock? skip = null;

            foreach (CacheBlock? item in blocks.Where(v => !v.IsLocked)
                .OrderByDescending(b => b.Length)
                .ToArray())
            {
                if (item.Start + item.Length < currentFrame)
                {
                    skip = item;
                }

                DeleteRange(item.Start, item.Start + item.Length);
                if (_size < _maxSize.Value)
                    return;
            }

            if (skip != null)
            {
                DeleteRange(skip.Start, skip.Start + skip.Length - 1);
            }
        }

        lock (_lock)
        {
            int loop = 5;
            FrameCacheDeletionStrategy strategy = Options.DeletionStrategy;

            while (_size >= _maxSize.Value && loop >= 0)
            {
                if (strategy == FrameCacheDeletionStrategy.BackwardBlock)
                {
                    DeleteBackwardBlock();
                    strategy = FrameCacheDeletionStrategy.Far;
                    if (_size < _maxSize.Value)
                    {
                        return;
                    }
                }

                long excess = _size - _maxSize.Value;
                int sizePerCache = CalculateBitmapByteSize(Options.GetSize(FrameSize), Options.ColorType == FrameCacheColorType.YUV);
                long targetCount = excess / sizePerCache;

                var items = Options.DeletionStrategy == FrameCacheDeletionStrategy.Old
                    ? GetOldCaches(targetCount)
                    : GetFarCaches(targetCount);
                DeleteItems(items);

                loop--;
            }
        }
    }

    private static int CalculateBitmapByteSize(PixelSize size, bool i420)
    {
        return i420 ? size.Width * (int)(size.Height * 1.5)
            : size.Width * size.Height * 4;
    }

    private static IEnumerable<KeyValuePair<int, CacheEntry>> GetRange(IEnumerable<KeyValuePair<int, CacheEntry>> source, int start, int end)
    {
        return source
            .SkipWhile(t => t.Key < start)
            .TakeWhile(t => t.Key < end);
    }
}
