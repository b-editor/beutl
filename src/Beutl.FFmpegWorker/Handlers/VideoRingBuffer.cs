using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegWorker.Decoding;
using Beutl.Media;

namespace Beutl.FFmpegWorker.Handlers;

internal struct RingBufferReadResult
{
    public bool Success;
    public int SlotIndex;
    public long SlotDataOffset;
    public int Width;
    public int Height;
    public int BytesPerPixel;
    public int DataLength;
    public bool IsHdr;
    public string? NewSharedMemoryName;
    public long NewSlotSize;
    public int NewSlotCount;
    public bool ColorSpaceChanged;
    public float[]? TransferFn;
    public float[]? ToXyzD50;
}

internal sealed class VideoRingBuffer : IDisposable
{
    private readonly int _slotCount;
    private long _slotSize;
    private readonly int[] _slotFrameNumbers;
    private readonly SlotMetadata[] _slots;
    private int _nextWriteSlot;
    private int _lastServedSlot = -1;
    public volatile int LastRequestedFrame = -1;

    // 色空間キャッシュ
    private BitmapColorSpace? _lastColorSpace;
    private float[]? _lastTransferFn;
    private float[]? _lastToXyzD50;

    // プリフェッチ制御
    private CancellationTokenSource? _prefetchCts;
    private Task? _prefetchTask;
    private readonly ManualResetEventSlim _prefetchSignal = new(false);

    // 外部依存
    private readonly FFmpegReader _reader;
    private readonly int _readerId;
    private readonly SemaphoreSlim _readerLock;
    private readonly Func<int> _nextShmGeneration;

    public SharedMemoryBuffer VideoBuffer { get; private set; }

    public VideoRingBuffer(
        int slotCount, long slotSize, SharedMemoryBuffer videoBuffer,
        FFmpegReader reader, int readerId,
        SemaphoreSlim readerLock, Func<int> nextShmGeneration)
    {
        _slotCount = slotCount;
        _slotSize = slotSize;
        VideoBuffer = videoBuffer;
        _reader = reader;
        _readerId = readerId;
        _readerLock = readerLock;
        _nextShmGeneration = nextShmGeneration;
        _slotFrameNumbers = new int[slotCount];
        _slots = new SlotMetadata[slotCount];
        Array.Fill(_slotFrameNumbers, -1);
    }

    // フレームを読み取る。ReaderLockを内部で取得・解放する。
    public RingBufferReadResult ReadFrame(int frame)
    {
        _readerLock.Wait();
        try
        {
            int hitSlot = FindSlot(frame);
            if (hitSlot >= 0)
                return HandleCacheHit(frame, hitSlot);

            return HandleCacheMiss(frame);
        }
        finally
        {
            _readerLock.Release();
        }
    }

    public void InvalidateAllSlots()
    {
        for (int i = 0; i < _slotFrameNumbers.Length; i++)
        {
            _slotFrameNumbers[i] = -1;
            _slots[i] = default;
        }

        _nextWriteSlot = 0;
        _lastServedSlot = -1;
    }

    // ReaderLockを保持せずにプリフェッチを停止する。
    public void StopPrefetch()
    {
        if (_prefetchCts != null)
        {
            _prefetchCts.Cancel();
            try { _prefetchTask?.Wait(); }
            catch (AggregateException) { }
            catch (OperationCanceledException) { }
            _prefetchCts.Dispose();
            _prefetchCts = null;
            _prefetchTask = null;
        }
    }

    public void Dispose()
    {
        StopPrefetch();
        _prefetchSignal.Dispose();
        VideoBuffer.Dispose();
    }

    private int FindSlot(int frame)
    {
        for (int i = 0; i < _slotFrameNumbers.Length; i++)
        {
            if (_slotFrameNumbers[i] == frame)
                return i;
        }

        return -1;
    }

    private RingBufferReadResult HandleCacheHit(int frame, int hitSlot)
    {
        ref var slotMeta = ref _slots[hitSlot];
        LastRequestedFrame = frame;
        _lastServedSlot = hitSlot;

        // 色空間情報
        bool colorSpaceChanged = UpdateColorSpaceCache(slotMeta.ColorSpace);

        // プリフェッチ再開シグナル
        _prefetchSignal.Set();

        return new RingBufferReadResult
        {
            Success = true,
            SlotIndex = hitSlot,
            SlotDataOffset = hitSlot * _slotSize,
            Width = slotMeta.Width,
            Height = slotMeta.Height,
            BytesPerPixel = slotMeta.BytesPerPixel,
            DataLength = slotMeta.DataLength,
            IsHdr = slotMeta.IsHdr,
            ColorSpaceChanged = colorSpaceChanged,
            TransferFn = colorSpaceChanged ? _lastTransferFn : null,
            ToXyzD50 = colorSpaceChanged ? _lastToXyzD50 : null,
        };
    }

    private RingBufferReadResult HandleCacheMiss(int frame)
    {
        // プリフェッチ停止
        StopPrefetchUnderLock();

        // ロック一時解放中にプリフェッチがこのフレームをデコードした可能性があるため再確認
        int hitSlot = FindSlot(frame);
        if (hitSlot >= 0)
            return HandleCacheHit(frame, hitSlot);

        // 共有メモリに直接デコード
        string? newShmName = null;
        var frameInfo = DecodeToSlot(frame, ref newShmName, out int writeSlot, out long offset);
        if (frameInfo == null)
        {
            LastRequestedFrame = frame;
            StartPrefetch();
            return new RingBufferReadResult { Success = false, NewSharedMemoryName = newShmName };
        }

        var fi = frameInfo.Value;

        // 色空間情報
        bool colorSpaceChanged = UpdateColorSpaceCache(fi.ColorSpace);

        // スロットメタデータ更新
        CommitSlot(writeSlot, frame, fi);
        LastRequestedFrame = frame;
        _lastServedSlot = writeSlot;

        // プリフェッチ開始
        StartPrefetch();

        return new RingBufferReadResult
        {
            Success = true,
            SlotIndex = writeSlot,
            SlotDataOffset = offset,
            Width = fi.Width,
            Height = fi.Height,
            BytesPerPixel = fi.BytesPerPixel,
            DataLength = fi.DataLength,
            IsHdr = fi.IsHdr,
            NewSharedMemoryName = newShmName,
            NewSlotSize = newShmName != null ? _slotSize : 0,
            NewSlotCount = newShmName != null ? _slotCount : 0,
            ColorSpaceChanged = colorSpaceChanged,
            TransferFn = colorSpaceChanged ? _lastTransferFn : null,
            ToXyzD50 = colorSpaceChanged ? _lastToXyzD50 : null,
        };
    }

    // Call with ReaderLock held.
    private bool UpdateColorSpaceCache(BitmapColorSpace colorSpace)
    {
        bool colorSpaceChanged = _lastColorSpace != colorSpace;
        _lastColorSpace = colorSpace;
        if (colorSpaceChanged || _lastToXyzD50 == null || _lastTransferFn == null)
        {
            (_lastTransferFn, _lastToXyzD50) = ColorSpaceIpcHelper.Extract(colorSpace);
        }

        return colorSpaceChanged;
    }

    // Call with ReaderLock held.
    private void CommitSlot(int slot, int frame, VideoFrameInfo info)
    {
        _slots[slot] = SlotMetadata.FromFrameInfo(frame, info);
        _slotFrameNumbers[slot] = frame;
        _nextWriteSlot = (slot + 1) % _slotCount;
    }

    // ReaderLock保持下で呼ぶこと。
    private unsafe VideoFrameInfo? DecodeToSlot(
        int frame, ref string? newShmName, out int writeSlot, out long offset)
    {
        writeSlot = _nextWriteSlot;
        offset = writeSlot * _slotSize;

        byte* ptr = VideoBuffer.AcquirePointer();
        try
        {
            var destination = new Span<byte>(ptr + offset, (int)_slotSize);

            if (_reader.ReadVideo(frame, destination, out var frameInfo))
                return frameInfo;

            // バッファが小さい場合リサイズして再試行
            if (frameInfo.DataLength > 0 && frameInfo.DataLength > _slotSize)
            {
                VideoBuffer.ReleasePointer();
                ptr = null;
                VideoBuffer.Dispose();
                _slotSize = frameInfo.DataLength + 64;
                long totalSize = _slotSize * _slotCount;
                int gen = _nextShmGeneration();
                newShmName = $"beutl-ffmpeg-video-{Environment.ProcessId}-{_readerId}-{gen}";
                VideoBuffer = SharedMemoryBuffer.Create(newShmName, totalSize);
                InvalidateAllSlots();

                writeSlot = 0;
                offset = 0;

                ptr = VideoBuffer.AcquirePointer();
                destination = new Span<byte>(ptr + offset, (int)_slotSize);

                if (_reader.ReadVideo(frame, destination, out frameInfo))
                    return frameInfo;
            }

            return null;
        }
        finally
        {
            if (ptr != null)
                VideoBuffer.ReleasePointer();
        }
    }

    // ReaderLock保持下で呼ぶこと。ロックを一時解放してプリフェッチスレッドの終了を待つ。
    private void StopPrefetchUnderLock()
    {
        if (_prefetchCts != null)
        {
            _prefetchCts.Cancel();
            _prefetchSignal.Set(); // Wait中のスレッドを起こす

            // ロックを一時解放してプリフェッチスレッドの終了を待つ
            _readerLock.Release();
            try { _prefetchTask?.Wait(); }
            catch (AggregateException) { }
            catch (OperationCanceledException) { }
            _readerLock.Wait();

            _prefetchCts.Dispose();
            _prefetchCts = null;
            _prefetchTask = null;
        }
    }

    // ReaderLock保持下で呼ぶこと。
    private void StartPrefetch()
    {
        _prefetchCts = new CancellationTokenSource();
        var ct = _prefetchCts.Token;
        _prefetchSignal.Reset();

        long totalFrames = _reader.HasVideo ? _reader.VideoInfo.NumFrames : 0;

        _prefetchTask = Task.Run(() =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int baseFrame = LastRequestedFrame;
                    if (baseFrame < 0) break;

                    // 既にバッファにあるフレーム数をカウント
                    int maxAhead = _slotCount - 1;
                    int cachedAhead = CountCachedAhead(baseFrame, maxAhead);

                    if (cachedAhead >= maxAhead)
                    {
                        // 十分プリフェッチ済み → 次のリクエストを待つ
                        _prefetchSignal.Wait(ct);
                        _prefetchSignal.Reset();
                        continue;
                    }

                    if (!FFmpegSeekDecision.HasPrefetchTarget(baseFrame, cachedAhead, totalFrames, out int nextFrame))
                        break;

                    // 既にキャッシュ済みならスキップ
                    if (FindSlot(nextFrame) >= 0) continue;

                    bool decodedAhead = false;
                    _readerLock.Wait(ct);
                    try
                    {
                        if (ct.IsCancellationRequested) break;
                        if (FindSlot(nextFrame) >= 0) continue;

                        decodedAhead = TryPrefetchUnderLock(nextFrame);
                    }
                    finally
                    {
                        _readerLock.Release();
                    }

                    if (!decodedAhead)
                    {
                        // Could not decode/store the next frame (EOF, undecodable, or larger than the
                        // slot). Wait for the next request instead of busy-retrying the same uncached
                        // frame, which would otherwise spin a CPU core under the reader lock.
                        _prefetchSignal.Wait(ct);
                        _prefetchSignal.Reset();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                WorkerLog.Error("Prefetch error", ex);
            }
        }, ct);
    }

    private int CountCachedAhead(int baseFrame, int maxAhead)
    {
        int cachedAhead = 0;
        for (int i = 1; i <= maxAhead; i++)
        {
            if (FindSlot(baseFrame + i) >= 0)
                cachedAhead++;
            else
                break;
        }

        return cachedAhead;
    }

    // Call with ReaderLock held.
    private unsafe bool TryPrefetchUnderLock(int nextFrame)
    {
        // LastServedSlotを上書きしないようにする
        int writeSlot = _nextWriteSlot;
        if (writeSlot == _lastServedSlot)
        {
            writeSlot = (writeSlot + 1) % _slotCount;
        }

        long slotOffset = writeSlot * _slotSize;
        byte* pfPtr = VideoBuffer.AcquirePointer();
        try
        {
            var destination = new Span<byte>(pfPtr + slotOffset, (int)_slotSize);

            if (_reader.ReadVideo(nextFrame, destination, out var pfInfo)
                && pfInfo.DataLength <= _slotSize)
            {
                CommitSlot(writeSlot, nextFrame, pfInfo);
                return true;
            }
        }
        finally
        {
            VideoBuffer.ReleasePointer();
        }

        return false;
    }

    internal struct SlotMetadata
    {
        public int FrameNumber;
        public int Width;
        public int Height;
        public int BytesPerPixel;
        public int DataLength;
        public bool IsHdr;
        public BitmapColorSpace ColorSpace;

        public static SlotMetadata FromFrameInfo(int frameNumber, VideoFrameInfo fi)
        {
            return new SlotMetadata
            {
                FrameNumber = frameNumber,
                Width = fi.Width,
                Height = fi.Height,
                BytesPerPixel = fi.BytesPerPixel,
                DataLength = fi.DataLength,
                IsHdr = fi.IsHdr,
                ColorSpace = fi.ColorSpace
            };
        }
    }
}
