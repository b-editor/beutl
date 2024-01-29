using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using Beutl.Configuration;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;

using OpenCvSharp;

using Reactive.Bindings;

namespace Beutl.Models;

public sealed class FrameCacheManager : IDisposable
{
    private readonly SortedDictionary<int, CacheEntry> _entries = [];
    private readonly object _lock = new();
    private readonly PixelSize _frameSize;
    private readonly ReadOnlyReactivePropertySlim<long> _maxSize;
    private long _size;

    public event Action<ImmutableArray<CacheBlock>>? BlocksUpdated;

    public FrameCacheManager(PixelSize frameSize, FrameCacheOptions options)
    {
        _frameSize = frameSize;
        Options = options;

        _maxSize = GlobalConfiguration.Instance.EditorConfig.GetObservable(EditorConfig.FrameCacheMaxSizeProperty)
            .Select(v => (long)(v * 1024 * 1024))
            .ToReadOnlyReactivePropertySlim();
    }

    public ImmutableArray<CacheBlock> Blocks { get; private set; }

    public FrameCacheOptions Options { get; set; }

    // 再生中のフレーム
    public int CurrentFrame { get; set; }

    public void Add(int frame, Ref<Bitmap<Bgra8888>> bitmap)
    {
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
                    _size -= e.ByteCount;
                    e.Dispose();
                }
            }

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
            foreach (KeyValuePair<int, CacheEntry> item in _entries
                .SkipWhile(t => t.Key < start)
                .TakeWhile(t => t.Key < end))
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
            return _entries
                .SkipWhile(t => t.Key < start)
                .TakeWhile(t => t.Key < end)
                .Sum(t => (long)t.Value.ByteCount);
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
        _maxSize.Dispose();
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
            BlocksUpdated?.Invoke([]);
        }
    }

    // |□■■■■□□□■□■■■□|
    // GetBlock() -> [(1, 4), (8, 1), (10, 3)]
    public ImmutableArray<CacheBlock> CalculateBlocks()
    {
        return CalculateBlocks(int.MinValue, int.MaxValue);
    }

    public ImmutableArray<CacheBlock> CalculateBlocks(int start, int end)
    {
        lock (_lock)
        {
            var list = new List<CacheBlock>();
            int blockStart = -1;
            int expect = 0;
            int count = 0;
            bool isLocked = false;
            //SortedSet<KeyValuePair<int, CacheEntry>> set = GetInnerSet(_entries);
            //: set.GetViewBetween(new(start, null!), new(end, null!));
            IEnumerable<KeyValuePair<int, CacheEntry>> items
                = start == int.MinValue && end == int.MaxValue
                    ? _entries
                    : _entries.SkipWhile(t => t.Key < start).TakeWhile(t => t.Key < end);

            foreach ((int key, CacheEntry item) in items)
            {
                if (blockStart == -1)
                {
                    blockStart = key;
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
                    list.Add(new(blockStart, count, isLocked));
                    blockStart = -1;
                    count = 0;

                    blockStart = key;
                    isLocked = item.IsLocked;
                    expect = key + 1;
                    count++;
                }
            }

            if (blockStart != -1)
            {
                list.Add(new(blockStart, count, isLocked));
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

    private void AutoDelete()
    {
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
                .Where(v => !v.Value.IsLocked && v.Key < CurrentFrame)
                .OrderBy(v => v.Key - CurrentFrame)
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
            ImmutableArray<CacheBlock> blocks = CalculateBlocks(int.MinValue, CurrentFrame);
            CacheBlock? skip = null;

            foreach (CacheBlock? item in blocks.Where(v => !v.IsLocked)
                .OrderByDescending(b => b.Length)
                .ToArray())
            {
                if (item.Start + item.Length < CurrentFrame)
                {
                    skip = item;
                }

                RemoveRange(item.Start, item.Start + item.Length);
                if (_size < _maxSize.Value)
                    return;
            }

            if (skip != null)
            {
                RemoveRange(skip.Start, skip.Start + skip.Length - 1);
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
                int sizePerCache = CalculateBitmapByteSize(Options.GetSize(_frameSize), Options.ColorType == FrameCacheColorType.YUV);
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

    // https://github.com/dotnet/runtime/issues/92633
    //[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_set")]
    //private static extern ref SortedSet<KeyValuePair<int, CacheEntry>> GetInnerSet(SortedDictionary<int, CacheEntry> self);

    public record CacheBlock(int Start, int Length, bool IsLocked);

    internal class CacheEntry : IDisposable
    {
        private Mat _mat;

        public CacheEntry(Ref<Bitmap<Bgra8888>> bitmap, FrameCacheOptions options)
        {
            using (Ref<Bitmap<Bgra8888>> t = bitmap.Clone())
            {
                _mat = ToYUV(t.Value, options);
            }

            LastAccessTime = DateTime.UtcNow;
        }

        public DateTime LastAccessTime { get; private set; }

        public int ByteCount => (int)(_mat.DataEnd - _mat.DataStart);

        public bool IsLocked { get; set; }

        public void SetBitmap(Ref<Bitmap<Bgra8888>> bitmap, FrameCacheOptions options)
        {
            _mat?.Dispose();
            using (Ref<Bitmap<Bgra8888>> t = bitmap.Clone())
            {
                _mat = ToYUV(t.Value, options);
            }

            LastAccessTime = DateTime.UtcNow;
        }

        public Ref<Bitmap<Bgra8888>> GetBitmap()
        {
            LastAccessTime = DateTime.UtcNow;
            return Ref<Bitmap<Bgra8888>>.Create(ToBitmap(_mat));
        }

        private static Mat ToYUV(Bitmap<Bgra8888> bitmap, FrameCacheOptions options)
        {
            var result = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC4, bitmap.Data);

            PixelSize size = new(bitmap.Width, bitmap.Height);
            PixelSize newSize = options.GetSize(size);
            if (newSize != size)
            {
                Mat tmp = result.Resize(new Size(newSize.Width, newSize.Height));
                result.Dispose();
                result = tmp;
            }

            if (options.ColorType == FrameCacheColorType.YUV)
            {
                var mat = new Mat((int)(result.Rows * 1.5), result.Cols, MatType.CV_8UC1);
                Cv2.CvtColor(result, mat, ColorConversionCodes.BGRA2YUV_I420);
                result.Dispose();
                result = mat;
            }

            return result;
        }

        private static unsafe Bitmap<Bgra8888> ToBitmap(Mat mat)
        {
            Bitmap<Bgra8888>? bitmap;
            if (mat.Type() == MatType.CV_8UC4)
            {
                bitmap = new Bitmap<Bgra8888>(mat.Width, mat.Height);
                Buffer.MemoryCopy((void*)mat.Data, (void*)bitmap.Data, bitmap.ByteCount, bitmap.ByteCount);
            }
            else
            {
                bitmap = new Bitmap<Bgra8888>(mat.Width, (int)(mat.Height / 1.5));
                using var bgra = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC4, bitmap.Data);

                Cv2.CvtColor(mat, bgra, ColorConversionCodes.YUV2BGRA_I420);
            }

            return bitmap;
        }

        public void Dispose()
        {
            _mat?.Dispose();
            _mat = null!;
        }
    }
}
