using Beutl.Extensibility;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegIpc.Transport;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.FFmpegWorker.Providers;

internal sealed class IpcSampleProvider : ISampleProvider
{
    private readonly IpcConnection _connection;
    private readonly SharedMemoryBuffer[] _audioBuffers;

    // 現在のキャッシュ（1秒分）
    private Pcm<Stereo32BitFloat>? _currentChunk;
    private long _currentChunkOffset;
    private int _currentBufferIndex;

    // 先行フェッチ（次の1秒分、バックグラウンド）
    private Task<Pcm<Stereo32BitFloat>>? _prefetchTask;
    private long _prefetchChunkOffset;
    private int _prefetchBufferIndex;

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
        if (_prefetchTask != null && _prefetchChunkOffset == chunkOffset)
        {
            var pcm = await _prefetchTask;
            _prefetchTask = null;

            _currentChunk?.Dispose();
            _currentChunk = pcm;
            _currentChunkOffset = chunkOffset;
            _currentBufferIndex = _prefetchBufferIndex;
            return;
        }

        // プリフェッチが進行中だが要求と一致しない場合は、完了を待ってから結果を破棄する。
        // ホストへ非順次なリクエストが流出しないよう、参照を捨てるだけでなく必ず await する。
        // キャンセルや通信エラーはそのまま上位へ伝播させる。
        if (_prefetchTask != null)
        {
            Task<Pcm<Stereo32BitFloat>> staleTask = _prefetchTask;
            _prefetchTask = null;
            Pcm<Stereo32BitFloat> stalePcm = await staleTask;
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
        if (_prefetchTask != null) return;
        if (_currentChunk == null) return;

        long nextChunkOffset = _currentChunkOffset + _currentChunk.NumSamples;
        if (nextChunkOffset >= SampleCount) return;

        _prefetchBufferIndex = 1 - _currentBufferIndex;
        _prefetchChunkOffset = nextChunkOffset;
        _prefetchTask = FetchChunk(nextChunkOffset, _prefetchBufferIndex).AsTask();
    }

    private async ValueTask<Pcm<Stereo32BitFloat>> FetchChunk(long chunkOffset, int bufferIndex)
    {
        long chunkLength = Math.Min(SampleRate, SampleCount - chunkOffset);

        var request = IpcMessage.Create(_connection.NextId(), MessageType.RequestSample,
            new RequestSampleMessage { Offset = chunkOffset, Length = chunkLength, BufferIndex = bufferIndex });
        var response = await _connection.SendAndReceiveAsync(request)
                       ?? throw new IOException("Connection closed while waiting for audio samples");

        if (response.Type == MessageType.CancelEncode)
            throw new OperationCanceledException();

        if (response.Error != null)
            throw new InvalidOperationException($"Sample failed: {response.Error}");

        var sampleInfo = response.GetPayload<ProvideSampleMessage>()
            ?? throw new InvalidOperationException("Missing payload for ProvideSample");

        var pcm = new Pcm<Stereo32BitFloat>((int)SampleRate, sampleInfo.NumSamples);
        unsafe
        {
            _audioBuffers[bufferIndex].Read(new Span<byte>((void*)pcm.Data, sampleInfo.DataLength));
        }

        return pcm;
    }
}
