using System.Diagnostics.CodeAnalysis;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegIpc.Transport;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;
using Microsoft.Extensions.Logging;

namespace Beutl.Extensions.FFmpeg.Decoding;

public sealed class FFmpegReaderProxy : MediaReader, IIdleSuspendableReader
{
    private readonly ILogger _logger = Log.CreateLogger<FFmpegReaderProxy>();
    private readonly OpenFileResponse _openResponse;
    private readonly Func<(IpcConnection Connection, OpenFileResponse Response)>? _reopen;
    private readonly FFmpegReaderIdleTracker? _idleTracker;
    // Serializes reads with suspension and disposal, which replace the worker reader and its buffers.
    private readonly Lock _gate = new();
    private IpcConnection _connection;
    private int _readerId;
    private string? _videoShmName;
    private string? _audioShmName;
    private SharedMemoryBuffer? _videoBuffer;
    private SharedMemoryBuffer? _audioBuffer;
    private BitmapColorSpace? _colorSpace;
    private int _ringSlotCount;
    private long _ringSlotSize;
    private volatile bool _suspended;
    private bool _closed;
    private bool _streamsChanged;
    private bool _reopenFailing;
    private Task? _pendingClose;
    private long _memoryBytes;
    private long _lastAccessTicks = Environment.TickCount64;

    internal FFmpegReaderProxy(
        IpcConnection connection,
        int readerId,
        OpenFileResponse openResponse,
        Func<(IpcConnection Connection, OpenFileResponse Response)>? reopen = null,
        FFmpegReaderIdleTracker? idleTracker = null)
    {
        _connection = connection;
        _readerId = readerId;
        _openResponse = openResponse;
        _reopen = reopen;
        _videoShmName = openResponse.VideoSharedMemoryName;
        _audioShmName = openResponse.AudioSharedMemoryName;
        _memoryBytes = openResponse.VideoRingBufferSlotSize * openResponse.VideoRingBufferSlotCount;

        if (openResponse.HasVideo)
        {
            var vi = openResponse;
            VideoInfo = new VideoStreamInfo(
                vi.VideoCodecName ?? "Unknown",
                vi.VideoNumFrames,
                new PixelSize(vi.VideoWidth, vi.VideoHeight),
                new Rational(vi.FrameRateNum, vi.FrameRateDen))
            {
                Duration = new Rational(vi.DurationNum, vi.DurationDen)
            };

            // 色空間復元
            _colorSpace = BuildColorSpace(openResponse);

            // リングバッファ情報
            _ringSlotCount = openResponse.VideoRingBufferSlotCount;
            _ringSlotSize = openResponse.VideoRingBufferSlotSize;
        }

        if (openResponse.HasAudio)
        {
            var ai = openResponse;
            AudioInfo = new AudioStreamInfo(
                ai.AudioCodecName ?? "Unknown",
                new Rational(ai.AudioDurationNum, ai.AudioDurationDen),
                ai.AudioSampleRate,
                ai.AudioNumChannels);
        }

        // Only video readers hold a decoder and ring buffer worth releasing while idle.
        if (openResponse.HasVideo && reopen != null && idleTracker != null)
        {
            _idleTracker = idleTracker;
            idleTracker.Track(this);
        }
    }

    public override VideoStreamInfo VideoInfo => field ?? throw new Exception("The stream does not exist.");

    public override AudioStreamInfo AudioInfo => field ?? throw new Exception("The stream does not exist.");

    public override bool HasVideo => _openResponse.HasVideo;

    public override bool HasAudio => _openResponse.HasAudio;

    internal bool IsSuspended => _suspended;

    internal int ReaderId => _readerId;

    // How long a read waits for the worker to close a suspended reader before logging that it is still waiting.
    internal TimeSpan CloseWarningDelay { get; init; } = TimeSpan.FromSeconds(5);

    long IIdleSuspendableReader.LastAccessTicks => Volatile.Read(ref _lastAccessTicks);

    long IIdleSuspendableReader.MemoryBytes => Volatile.Read(ref _memoryBytes);

    bool IIdleSuspendableReader.IsSuspended => _suspended;

    bool IIdleSuspendableReader.TrySuspend(long expectedLastAccessTicks)
    {
        if (_reopen == null || !_gate.TryEnter())
            return false;

        try
        {
            if (_closed || _suspended || Volatile.Read(ref _lastAccessTicks) != expectedLastAccessTicks)
                return false;

            _pendingClose = CloseWorkerReader(_connection, _readerId);
            _videoBuffer?.Dispose();
            _videoBuffer = null;
            _audioBuffer?.Dispose();
            _audioBuffer = null;
            _suspended = true;
            _logger.LogDebug("Suspended idle FFmpeg reader {ReaderId}", _readerId);
            return true;
        }
        finally
        {
            _gate.Exit();
        }
    }

    public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
    {
        lock (_gate)
        {
            try
            {
                if (!TryResume())
                {
                    image = null;
                    return false;
                }

                return ReadVideoCore(frame, out image);
            }
            finally
            {
                MarkAccessed();
            }
        }
    }

    private unsafe bool ReadVideoCore(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
    {
        var request = new ReadVideoRequest { ReaderId = _readerId, Frame = frame };
        var response = _connection.RequestAsync<ReadVideoRequest, ReadVideoResponse>(
            MessageType.ReadVideo, MessageType.ReadVideoResult, request).AsTask().GetAwaiter().GetResult();

        if (!response.Success)
        {
            image = null;
            return false;
        }

        // リサイズによりスロットサイズが変更された場合は更新
        if (response.RingBufferSlotSize.HasValue)
            _ringSlotSize = response.RingBufferSlotSize.Value;
        if (response.RingBufferSlotCount.HasValue)
            _ringSlotCount = response.RingBufferSlotCount.Value;
        Volatile.Write(ref _memoryBytes, _ringSlotSize * _ringSlotCount);

        // 共有メモリから読み取り（Worker側でリサイズされた場合は名前が変わる）
        EnsureVideoBuffer(response, response.SharedMemoryName);

        // 色空間情報は差分送信: Worker側から送られた場合のみキャッシュを更新
        if (response.TransferFn != null && response.ToXyzD50 != null)
        {
            _colorSpace = BuildColorSpaceFromArrays(response.TransferFn, response.ToXyzD50);
        }
        var colorSpace = _colorSpace ?? BitmapColorSpace.Srgb;

        bool isHdr = response.BytesPerPixel == 8;
        var colorType = isHdr ? BitmapColorType.Rgba16161616 : BitmapColorType.Bgra8888;
        int rowBytes = response.Width * response.BytesPerPixel;

        // ゼロコピー: 共有メモリを直接ポインタで参照するBitmapを作成
        var buffer = _videoBuffer!;
        byte* ptr = buffer.AcquirePointer();
        try
        {
            long readOffset = response.SlotDataOffset;
            var bmp = new Bitmap(
                (IntPtr)(ptr + readOffset), response.Width, response.Height, rowBytes,
                colorType, BitmapAlphaType.Unpremul, colorSpace);

            image = Ref<Bitmap>.Create(bmp, onRelease: buffer.ReleasePointer);
            return true;
        }
        catch
        {
            buffer.ReleasePointer();
            throw;
        }
    }

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
    {
        lock (_gate)
        {
            try
            {
                if (!TryResume())
                {
                    sound = null;
                    return false;
                }

                int sampleRate = AudioInfo.SampleRate;

                // SampleRate(1秒分)を超える場合はリクエストを分割
                if (length > sampleRate)
                {
                    return ReadAudioChunked(start, length, sampleRate, out sound);
                }

                return ReadAudioCore(start, length, out sound);
            }
            finally
            {
                MarkAccessed();
            }
        }
    }

    private unsafe bool ReadAudioCore(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
    {
        var request = new ReadAudioRequest { ReaderId = _readerId, Start = start, Length = length };
        var response = _connection.RequestAsync<ReadAudioRequest, ReadAudioResponse>(
            MessageType.ReadAudio, MessageType.ReadAudioResult, request).AsTask().GetAwaiter().GetResult();

        if (!response.Success)
        {
            sound = null;
            return false;
        }

        EnsureAudioBuffer(response.DataLength, response.SharedMemoryName);

        // ゼロコピー: 共有メモリを直接ポインタで参照するPcmを作成
        var buffer = _audioBuffer!;
        byte* ptr = buffer.AcquirePointer();
        try
        {
            var pcm = new Pcm<Stereo32BitFloat>(response.SampleRate, response.NumSamples, (IntPtr)ptr);
            sound = Ref<IPcm>.Create(pcm, onRelease: buffer.ReleasePointer);
            return true;
        }
        catch
        {
            buffer.ReleasePointer();
            throw;
        }
    }

    private bool ReadAudioChunked(int start, int length, int chunkSize, [NotNullWhen(true)] out Ref<IPcm>? sound)
    {
        var scratch = new Pcm<Stereo32BitFloat>(AudioInfo.SampleRate, length);
        try
        {
            int offset = 0;
            while (offset < length)
            {
                int currentChunk = Math.Min(chunkSize, length - offset);

                if (!ReadAudioCore(start + offset, currentChunk, out Ref<IPcm>? chunkRef))
                {
                    // A genuine failure (IPC error / disposed) aborts the whole request.
                    scratch.Dispose();
                    sound = null;
                    return false;
                }

                using (chunkRef)
                {
                    var chunk = (Pcm<Stereo32BitFloat>)chunkRef.Value;
                    // NumSamples == 0 is the ReadAudio end-of-stream signal: stop without advancing
                    // offset and report exactly the frames decoded so far.
                    if (chunk.NumSamples == 0)
                        break;

                    chunk.DataSpan.CopyTo(scratch.DataSpan.Slice(offset, chunk.NumSamples));
                    offset += chunk.NumSamples;
                }
            }

            if (offset == length)
            {
                sound = Ref<IPcm>.Create(scratch);
            }
            else
            {
                // Trim the trailing over-allocation so callers see the real decoded length
                // (NumSamples == offset), not a zero-filled tail.
                var trimmed = new Pcm<Stereo32BitFloat>(AudioInfo.SampleRate, offset);
                scratch.DataSpan.Slice(0, offset).CopyTo(trimmed.DataSpan);
                scratch.Dispose();
                sound = Ref<IPcm>.Create(trimmed);
            }
            return true;
        }
        catch
        {
            scratch.Dispose();
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
            {
                if (!_closed)
                {
                    _closed = true;
                    if (!_suspended)
                        CloseWorkerReader(_connection, _readerId);

                    _videoBuffer?.Dispose();
                    _videoBuffer = null;
                    _audioBuffer?.Dispose();
                    _audioBuffer = null;
                }
            }

            // After _closed is set, so a read resuming concurrently cannot register this reader again.
            _idleTracker?.Untrack(this);
        }

        base.Dispose(disposing);
    }

    private void MarkAccessed()
    {
        Volatile.Write(ref _lastAccessTicks, Environment.TickCount64);
    }

    // Returns false when the reader cannot produce media: it was disposed, a suspended reader cannot be
    // reopened, or the file was replaced while suspended (callers keep VideoInfo/AudioInfo from the first
    // open, so frames of a file with other stream properties would be read at the wrong positions).
    private bool TryResume()
    {
        if (_closed || _streamsChanged)
            return false;

        if (!_suspended)
            return true;

        // A read right after the sweep would otherwise open the new decoder while the worker still holds
        // the old one, the very allocation suspension exists to avoid under memory pressure.
        // Returning false instead would drop this frame (an export saves it without the clip). The close
        // finishes once the worker's in-flight decode does, or fails with the connection.
        if (_pendingClose is { } close)
        {
            if (!close.Wait(CloseWarningDelay))
            {
                _logger.LogWarning("Waiting for the worker to close FFmpeg reader {ReaderId} before reopening it", _readerId);
                close.Wait();
            }

            _pendingClose = null;
        }

        IpcConnection connection;
        OpenFileResponse response;
        try
        {
            (connection, response) = _reopen!();
        }
        catch (Exception ex)
        {
            // Stay suspended so a later read retries (e.g. once a moved file is back); a read runs every
            // frame, so only the first failure in a row is logged.
            if (!_reopenFailing)
                _logger.LogWarning(ex, "Failed to reopen suspended FFmpeg reader {ReaderId}", _readerId);

            _reopenFailing = true;
            return false;
        }

        _reopenFailing = false;
        if (!HasSameStreams(_openResponse, response))
        {
            CloseWorkerReader(connection, response.ReaderId);
            _streamsChanged = true;
            _logger.LogWarning(
                "The media file of FFmpeg reader {ReaderId} changed its streams while the reader was suspended; it no longer returns media",
                _readerId);
            return false;
        }

        _connection = connection;
        _readerId = response.ReaderId;
        _videoShmName = response.VideoSharedMemoryName;
        _audioShmName = response.AudioSharedMemoryName;
        if (response.HasVideo)
        {
            _colorSpace = BuildColorSpace(response);
            _ringSlotCount = response.VideoRingBufferSlotCount;
            _ringSlotSize = response.VideoRingBufferSlotSize;
            Volatile.Write(ref _memoryBytes, _ringSlotSize * _ringSlotCount);
        }

        // The tracker drops suspended readers, so clear the flag before registering again.
        _suspended = false;
        _idleTracker?.Track(this);
        _logger.LogDebug("Resumed FFmpeg reader as {ReaderId}", _readerId);
        return true;
    }

    internal static bool HasSameStreams(OpenFileResponse original, OpenFileResponse reopened)
    {
        if (original.HasVideo != reopened.HasVideo || original.HasAudio != reopened.HasAudio)
            return false;

        if (original.HasVideo
            && (original.VideoCodecName != reopened.VideoCodecName
                || original.VideoNumFrames != reopened.VideoNumFrames
                || original.VideoWidth != reopened.VideoWidth
                || original.VideoHeight != reopened.VideoHeight
                || original.FrameRateNum != reopened.FrameRateNum
                || original.FrameRateDen != reopened.FrameRateDen
                || original.DurationNum != reopened.DurationNum
                || original.DurationDen != reopened.DurationDen))
        {
            return false;
        }

        return !original.HasAudio
            || (original.AudioCodecName == reopened.AudioCodecName
                && original.AudioSampleRate == reopened.AudioSampleRate
                && original.AudioNumChannels == reopened.AudioNumChannels
                && original.AudioDurationNum == reopened.AudioDurationNum
                && original.AudioDurationDen == reopened.AudioDurationDen);
    }

    // The returned task never faults; the worker answers only after disposing the reader.
    private Task CloseWorkerReader(IpcConnection connection, int readerId)
    {
        // fire-and-forget: UIスレッドからの呼び出しでデッドロックしないよう
        // 同期ブロックを避けて非同期で送信
        return Task.Run(async () =>
        {
            try
            {
                await connection.SendAndReceiveAsync(
                    IpcMessage.Create(connection.NextId(), MessageType.CloseReader,
                        new CloseReaderRequest { ReaderId = readerId }));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to close FFmpeg reader {ReaderId} on worker", readerId);
            }
        });
    }

    private void EnsureVideoBuffer(ReadVideoResponse response, string? newShmName)
    {
        bool nameChanged = newShmName != null;
        if (nameChanged)
            _videoShmName = newShmName;

        // リングバッファの場合: 全体サイズで確保
        long requiredCapacity = _ringSlotCount > 0
            ? _ringSlotSize * _ringSlotCount
            : response.DataLength;

        if (!nameChanged && _videoBuffer != null && _videoBuffer.Capacity >= requiredCapacity)
            return;

        _videoBuffer?.Dispose();
        string shmName = _videoShmName
            ?? throw new InvalidOperationException("Video shared memory name not provided");
        _videoBuffer = SharedMemoryBuffer.Open(shmName, requiredCapacity);
    }

    private void EnsureAudioBuffer(int requiredSize, string? newShmName)
    {
        bool nameChanged = newShmName != null;
        if (nameChanged)
            _audioShmName = newShmName;

        if (!nameChanged && _audioBuffer != null && _audioBuffer.Capacity >= requiredSize)
            return;

        _audioBuffer?.Dispose();
        string shmName = _audioShmName
            ?? throw new InvalidOperationException("Audio shared memory name not provided");
        _audioBuffer = SharedMemoryBuffer.Open(shmName, requiredSize);
    }

    private static BitmapColorSpace? BuildColorSpace(OpenFileResponse response)
    {
        if (response.IccProfile != null)
        {
            return BitmapColorSpace.CreateIcc(response.IccProfile);
        }

        if (response.TransferFn != null && response.ToXyzD50 != null)
        {
            return BuildColorSpaceFromArrays(response.TransferFn, response.ToXyzD50);
        }

        return BitmapColorSpace.Srgb;
    }

    private static BitmapColorSpace BuildColorSpaceFromArrays(float[] transferFn, float[] toXyzD50)
    {
        if (transferFn.Length < 7 || toXyzD50.Length < 9)
            return BitmapColorSpace.Srgb;

        var fn = new BitmapColorSpaceTransferFn
        {
            G = transferFn[0],
            A = transferFn[1],
            B = transferFn[2],
            C = transferFn[3],
            D = transferFn[4],
            E = transferFn[5],
            F = transferFn[6]
        };
        var xyz = BitmapColorSpaceXyz.Create(toXyzD50);

        return BitmapColorSpace.CreateRgb(fn, xyz);
    }
}
