using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Beutl.Media;
using Beutl.Media.Source;
using Reactive.Bindings;

namespace Beutl.Models;

public sealed partial class FrameCacheManager : IDisposable
{
    private readonly SortedDictionary<int, CacheEntry> _entries = [];
    private readonly object _lock = new();
    private readonly ReadOnlyReactivePropertySlim<long> _maxSize;
    private long _size;
    private FrameCacheOptions _options;

    public event Action<ImmutableArray<CacheBlock>>? BlocksUpdated;

    /// <summary>Creates a frame cache for frames of <paramref name="frameSize"/>.</summary>
    /// <param name="maxSize">Cache budget in bytes. Must publish a value on subscribe.</param>
    public FrameCacheManager(PixelSize frameSize, FrameCacheOptions options, IObservable<long> maxSize)
    {
        ArgumentNullException.ThrowIfNull(maxSize);
        FrameSize = frameSize;
        _options = options;
        _maxSize = maxSize.ToReadOnlyReactivePropertySlim();
    }

    public ImmutableArray<CacheBlock> Blocks { get; private set; } = [];

    /// <summary>
    /// Cache-entry format. Assigning options that change the stored representation drops every entry:
    /// the existing ones were encoded under the previous options, and mixing the two makes playback
    /// alternate between resolutions.
    /// </summary>
    public FrameCacheOptions Options
    {
        get => _options;
        set
        {
            FrameCacheOptions old;
            lock (_lock)
            {
                old = _options;
                _options = value;
            }

            if (!old.ProducesSameCacheData(value, FrameSize))
            {
                Clear();
            }
        }
    }

    // 再生中のフレーム
    public int CurrentFrame { get; set; }

    public bool IsEnabled { get; set; }

    public PixelSize FrameSize { get; }

    public void Add(int frame, Ref<Bitmap> bitmap)
    {
        if (!IsEnabled) return;

        lock (_lock)
        {
            if (_entries.TryGetValue(frame, out CacheEntry? old))
            {
                // Locked entries are excluded from _size (see Lock), so only this branch adjusts it.
                if (!old.IsLocked)
                {
                    _size -= old.ByteCount;
                    old.SetBitmap(bitmap, Options);
                    _size += old.ByteCount;
                }
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

    public bool TryGet(int frame, [MaybeNullWhen(false)] out Ref<Bitmap> bitmap)
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
            foreach (KeyValuePair<int, CacheEntry> item in GetRange(_entries, start, end))
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
            foreach (KeyValuePair<int, CacheEntry> item in GetRange(_entries, start, end))
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

    private volatile bool _isDisposed;

    public bool IsDisposed => _isDisposed;

    public void Dispose()
    {
        _isDisposed = true;
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
            Blocks = [];
            BlocksUpdated?.Invoke(Blocks);
        }
    }

    private void AutoDelete()
    {
        // Queued by Add via Task.Run, so it can still be scheduled after Dispose released _maxSize.
        if (_isDisposed) return;

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
            int countBefore = _entries.Count;
            try
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
                    int sizePerCache = Math.Max(1, CalculateBitmapByteSize(
                        Options.GetSize(FrameSize), Options.ColorType == FrameCacheColorType.YUV));
                    // At least one, so an excess smaller than a single entry still makes progress
                    // instead of spinning the loop out with an empty deletion set.
                    long targetCount = Math.Max(1, excess / sizePerCache);

                    KeyValuePair<int, CacheEntry>[] items = strategy == FrameCacheDeletionStrategy.Old
                        ? GetOldCaches(targetCount)
                        : GetFarCaches(targetCount);
                    DeleteItems(items);

                    loop--;
                }
            }
            finally
            {
                // Eviction is the only removal path that does not go through DeleteAndUpdateBlocks,
                // so without this the timeline keeps painting evicted ranges as cached.
                if (_entries.Count != countBefore)
                {
                    UpdateBlocks();
                }
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
