using Beutl.Extensibility;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegIpc.Transport;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.FFmpegIpc.Providers;

internal sealed class IpcSampleProvider : ISampleProvider
{
    // Stereo32BitFloat destination: 2 channels * 4 bytes. Must match the Pcm<Stereo32BitFloat> allocated below.
    private const int Stereo32BitFloatBytesPerSample = 8;

    private readonly IpcConnection _connection;
    private readonly SharedMemoryBuffer[] _audioBuffers;

    // 現在のキャッシュ（1秒分）
    private Pcm<Stereo32BitFloat>? _currentChunk;
    private long _currentChunkOffset;
    private int _currentBufferIndex;

    // 先行フェッチ（次の1秒分、バックグラウンド）
    private readonly PrefetchSlot<long, Pcm<Stereo32BitFloat>> _prefetch = new();
    private long _nextSample;
    private volatile bool _disposed;

    public IpcSampleProvider(IpcConnection connection, SharedMemoryBuffer[] audioBuffers,
        long sampleCount, long sampleRate)
    {
        _connection = connection;
        _audioBuffers = audioBuffers;
        SampleCount = sampleCount;
        SampleRate = sampleRate;
    }

    public long SampleCount { get; }
    public long SampleRate { get; }

    public async ValueTask<Pcm<Stereo32BitFloat>?> ReadNextSamples(int length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long offset = _nextSample;
        if (offset >= SampleCount)
            return null;

        // Always return a Pcm of the requested length, zero-filling past the timeline end: the encoder encodes
        // frame.NbSamples even from a shorter Pcm, so a short one would leave stale samples in the final frame.
        long availableLength = Math.Min(length, SampleCount - offset);
        Pcm<Stereo32BitFloat> pcm;
        if (availableLength == length)
        {
            pcm = await SampleExact(offset, length);
        }
        else
        {
            using var filled = await SampleExact(offset, availableLength);
            pcm = new Pcm<Stereo32BitFloat>((int)SampleRate, length);
            filled.DataSpan.CopyTo(pcm.DataSpan);
        }

        _nextSample = offset + length;
        return pcm;
    }

    // Fills exactly `length` samples starting at `offset`. The caller guarantees the whole range is within
    // the timeline (offset + length <= SampleCount), so the chunk loader never slices past EOF.
    private async ValueTask<Pcm<Stereo32BitFloat>> SampleExact(long offset, long length)
    {
        // キャッシュに前半が含まれているか確認
        await EnsureChunkLoaded(offset);

        long cacheEnd = _currentChunkOffset + _currentChunk!.NumSamples;
        if (offset + length <= cacheEnd)
        {
            // チャンクロード後に完全に収まる場合
            var pcm = CopyFromCache(offset, length);
            StartPrefetchIfNeeded();
            return pcm;
        }

        // チャンク境界をまたぐ場合: 前半を現在のチャンクからコピー
        long firstPartLength = cacheEnd - offset;
        var result2 = new Pcm<Stereo32BitFloat>((int)SampleRate, (int)length);
        try
        {
            // Test-only probe, inert in production (see CrossChunkSplitAllocatedForTest).
            CrossChunkSplitAllocatedForTest?.Invoke(result2);

            int start = (int)(offset - _currentChunkOffset);
            _currentChunk.DataSpan.Slice(start, (int)firstPartLength).CopyTo(result2.DataSpan);

            // 後半を次のチャンクからコピー
            long remainingOffset = cacheEnd;
            long remainingLength = length - firstPartLength;
            await EnsureChunkLoaded(remainingOffset);
            _currentChunk!.DataSpan[..(int)remainingLength].CopyTo(result2.DataSpan[(int)firstPartLength..]);

            StartPrefetchIfNeeded();
            return result2;
        }
        catch
        {
            // EnsureChunkLoaded can throw (IPC error / cancellation) after result2 is allocated; dispose
            // its native buffer so a failed split does not leak it. The success path returns it undisposed.
            result2.Dispose();
            throw;
        }
    }

    private Pcm<Stereo32BitFloat> CopyFromCache(long offset, long length)
    {
        int start = (int)(offset - _currentChunkOffset);
        var result = new Pcm<Stereo32BitFloat>((int)SampleRate, (int)length);
        _currentChunk!.DataSpan.Slice(start, (int)length).CopyTo(result.DataSpan);
        return result;
    }

    private async ValueTask EnsureChunkLoaded(long offset)
    {
        long chunkOffset = (offset / SampleRate) * SampleRate;

        // 既にロード済みのチャンクが要求と一致する場合は何もしない
        if (_currentChunk != null && _currentChunkOffset == chunkOffset)
        {
            return;
        }

        // Reads are in order, so an armed prefetch is always for this chunk.
        Task<Pcm<Stereo32BitFloat>>? prefetched = _prefetch.TryConsumeMatching(chunkOffset, out int prefetchBufferIndex);
        if (prefetched != null)
        {
            var pcm = await prefetched;

            _currentChunk?.Dispose();
            _currentChunk = pcm;
            _currentChunkOffset = chunkOffset;
            _currentBufferIndex = prefetchBufferIndex;
            return;
        }

        int bufferIndex = 0;
        Pcm<Stereo32BitFloat> chunk = await FetchChunk(chunkOffset, bufferIndex);
        _currentChunk?.Dispose();
        _currentChunk = chunk;
        _currentChunkOffset = chunkOffset;
        _currentBufferIndex = bufferIndex;
    }

    private void StartPrefetchIfNeeded()
    {
        if (_prefetch.HasPrefetch) return;
        if (_currentChunk == null) return;

        long nextChunkOffset = _currentChunkOffset + _currentChunk.NumSamples;
        if (nextChunkOffset >= SampleCount) return;

        int prefetchBufferIndex = 1 - _currentBufferIndex;
        _prefetch.Arm(nextChunkOffset, prefetchBufferIndex, FetchChunk(nextChunkOffset, prefetchBufferIndex).AsTask());
    }

    private async ValueTask<Pcm<Stereo32BitFloat>> FetchChunk(long chunkOffset, int bufferIndex)
    {
        long chunkLength = Math.Min(SampleRate, SampleCount - chunkOffset);

        var request = IpcMessage.Create(_connection.NextId(), MessageType.RequestSample,
            new RequestSampleMessage { Offset = chunkOffset, Length = chunkLength, BufferIndex = bufferIndex });
        var response = await _connection.SendAndReceiveAsync(request);

        // SendAndReceiveAsync surfaces a closed connection as IOException, an error response as
        // FFmpegWorkerException, and a host CancelEncode as OperationCanceledException, so the response here
        // is always a live ProvideSample for this request.
        var sampleInfo = response.RequirePayload<ProvideSampleMessage>(MessageType.ProvideSample);

        // SharedMemoryBuffer.Read copies the worker-reported DataLength bytes into the native Pcm, whose
        // capacity is NumSamples * Stereo32BitFloatBytesPerSample. Validate the reported size against that
        // capacity before reading so an oversized (or negative) DataLength can't overrun the allocation.
        if (sampleInfo.NumSamples < 0)
            throw new InvalidOperationException(
                $"Sample chunk has a negative NumSamples {sampleInfo.NumSamples}.");

        long expected = (long)sampleInfo.NumSamples * Stereo32BitFloatBytesPerSample;
        if (sampleInfo.DataLength != expected)
            throw new InvalidOperationException(
                $"Sample DataLength {sampleInfo.DataLength} does not match the {sampleInfo.NumSamples}-sample " +
                $"Stereo32BitFloat buffer size {expected}.");

        var pcm = new Pcm<Stereo32BitFloat>((int)SampleRate, sampleInfo.NumSamples);
        try
        {
            unsafe
            {
                _audioBuffers[bufferIndex].Read(new Span<byte>((void*)pcm.Data, (int)expected));
            }

            return pcm;
        }
        catch
        {
            pcm.Dispose();
            throw;
        }
    }

    // Test-only probe: lets a Dispose test wait until the in-flight prefetch has actually faulted before
    // dropping it, so the faulted-task path is exercised deterministically without a sleep.
    internal bool IsPrefetchFaultedForTest() => _prefetch.IsFaulted;

    // Test-only hook (null and inert in production): invoked with the cross-chunk split's freshly
    // allocated Pcm before the second chunk is loaded, so a disposal test can assert that a mid-split
    // fault frees that buffer instead of leaking its native memory.
    internal Action<Pcm<Stereo32BitFloat>>? CrossChunkSplitAllocatedForTest { get; set; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Don't wait on a prefetch whose connection may be closing: observe its fault, or dispose the native
        // Pcm a successful one holds. ExecuteSynchronously runs this on the completing thread; drop it if the
        // body grows beyond a single free.
        _prefetch.Detach()?.ContinueWith(
            static t =>
            {
                if (t.IsFaulted)
                    _ = t.Exception;
                else if (t.IsCompletedSuccessfully)
                    t.Result.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        _currentChunk?.Dispose();
        _currentChunk = null;
    }
}
