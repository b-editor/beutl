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
    private bool _disposed;

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
    public long SamplesProvided { get; private set; }

    public async ValueTask<Pcm<Stereo32BitFloat>> Sample(long offset, long length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Honor the ISampleProvider convention (see Beutl.Models.SampleProviderImpl.Sample): always return a
        // Pcm of the requested length, zero-filling any samples past the timeline end. The encoder issues the
        // final Sample with frame.NbSamples, which can straddle EOF; FFmpegEncodingController.GetAudioFrame
        // copies pcm.NumSamples into a fixed-size frame but still encodes frame.NbSamples, so a short Pcm
        // would leave stale tail samples in the final frame. Reading past SampleCount would also slice
        // out-of-range chunk data (ArgumentOutOfRangeException), so we fill only the available prefix and
        // leave the rest silent.
        long availableLength = offset >= SampleCount ? 0 : Math.Min(length, SampleCount - offset);
        if (availableLength <= 0)
            return new Pcm<Stereo32BitFloat>((int)SampleRate, (int)length);

        if (availableLength == length)
            return await SampleExact(offset, length);

        using var filled = await SampleExact(offset, availableLength);
        var padded = new Pcm<Stereo32BitFloat>((int)SampleRate, (int)length);
        filled.DataSpan.CopyTo(padded.DataSpan);
        return padded;
    }

    // Fills exactly `length` samples starting at `offset`. The caller guarantees the whole range is within
    // the timeline (offset + length <= SampleCount), so the chunk loader never slices past EOF.
    private async ValueTask<Pcm<Stereo32BitFloat>> SampleExact(long offset, long length)
    {
        // キャッシュヒット: 要求範囲がキャッシュ内に完全に収まる
        if (_currentChunk != null
            && offset >= _currentChunkOffset
            && offset + length <= _currentChunkOffset + _currentChunk.NumSamples)
        {
            var result = CopyFromCache(offset, length);
            StartPrefetchIfNeeded();
            return result;
        }

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
        int start = (int)(offset - _currentChunkOffset);
        _currentChunk.DataSpan.Slice(start, (int)firstPartLength).CopyTo(result2.DataSpan);

        // 後半を次のチャンクからコピー
        long remainingOffset = cacheEnd;
        long remainingLength = length - firstPartLength;
        await EnsureChunkLoaded(remainingOffset);
        _currentChunk!.DataSpan[..(int)remainingLength].CopyTo(result2.DataSpan[(int)firstPartLength..]);

        SamplesProvided += result2.NumSamples;
        StartPrefetchIfNeeded();
        return result2;
    }

    private Pcm<Stereo32BitFloat> CopyFromCache(long offset, long length)
    {
        int start = (int)(offset - _currentChunkOffset);
        var result = new Pcm<Stereo32BitFloat>((int)SampleRate, (int)length);
        _currentChunk!.DataSpan.Slice(start, (int)length).CopyTo(result.DataSpan);
        SamplesProvided += result.NumSamples;
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

        // プリフェッチ済みのチャンクと一致する場合
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

        // プリフェッチが進行中だが要求と一致しない場合は、完了を待ってから結果を破棄する。
        // ホストへ非順次なリクエストが流出しないよう、参照を捨てるだけでなく必ず await する。
        Task<Pcm<Stereo32BitFloat>>? stale = _prefetch.TryDetachStale(chunkOffset);
        if (stale != null)
        {
            Pcm<Stereo32BitFloat> stalePcm = await stale;
            stalePcm.Dispose();
        }

        int bufferIndex = 0;
        _currentChunk?.Dispose();
        _currentChunk = await FetchChunk(chunkOffset, bufferIndex);
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
        var sampleInfo = response.GetPayload<ProvideSampleMessage>()
            ?? throw new InvalidOperationException("Missing payload for ProvideSample");

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // A prefetch may still be in flight when the encode is torn down (cancel, error, normal end). The
        // connection/pipe it talks to may already be closing, so we must NOT synchronously wait on it here:
        // .Wait()/.GetAwaiter().GetResult() could deadlock or rethrow the pipe-teardown fault. Attach a
        // continuation that observes a fault (preventing UnobservedTaskException) and disposes the native Pcm
        // a successful prefetch would otherwise leak. Owning the connection is the caller's job, so we only
        // neutralize our own task. Returns promptly regardless of when the prefetch completes.
        //
        // The continuation handles all three terminal states: Faulted (observe the exception so it is not
        // unobserved), CompletedSuccessfully (dispose the native Pcm the result holds), and Canceled (yields
        // no result and no fault, so neither branch fires — cancellation is not an unobserved exception).
        // ExecuteSynchronously is safe because t.Result.Dispose() is a single P/Invoke free: no lock
        // contention and no throw path. If that body ever becomes heavier, drop ExecuteSynchronously so the
        // runtime schedules the continuation on a pool thread instead of the completing (receive-loop) one.
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
