using System.Diagnostics.CodeAnalysis;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegIpc.Transport;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Extensions.FFmpeg.Decoding;

public sealed class FFmpegReaderProxy : MediaReader
{
    private readonly IpcConnection _connection;
    private readonly int _readerId;
    private readonly OpenFileResponse _openResponse;
    private SharedMemoryBuffer? _videoBuffer;
    private SharedMemoryBuffer? _audioBuffer;
    private BitmapColorSpace? _colorSpace;
    private readonly int _ringSlotCount;
    private readonly long _ringSlotSize;

    internal FFmpegReaderProxy(IpcConnection connection, int readerId, OpenFileResponse openResponse)
    {
        _connection = connection;
        _readerId = readerId;
        _openResponse = openResponse;

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
    }

    public override VideoStreamInfo VideoInfo => field ?? throw new Exception("The stream does not exist.");

    public override AudioStreamInfo AudioInfo => field ?? throw new Exception("The stream does not exist.");

    public override bool HasVideo => _openResponse.HasVideo;

    public override bool HasAudio => _openResponse.HasAudio;

    public override unsafe bool ReadVideo(int frame, [NotNullWhen(true)] out Bitmap? image)
    {
        var request = new ReadVideoRequest { ReaderId = _readerId, Frame = frame };
        var response = _connection.RequestAsync<ReadVideoRequest, ReadVideoResponse>(
            MessageType.ReadVideo, MessageType.ReadVideoResult, request).AsTask().GetAwaiter().GetResult();

        if (!response.Success)
        {
            image = null;
            return false;
        }

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
        var bmp = new Bitmap(response.Width, response.Height, colorType, BitmapAlphaType.Unpremul, colorSpace);

        try
        {
            // リングバッファ: SlotDataOffset からの読み取り
            long readOffset = response.SlotDataOffset;
            _videoBuffer!.Read(new Span<byte>((void*)bmp.Data, response.DataLength), readOffset);
            image = bmp;
            return true;
        }
        catch
        {
            bmp.Dispose();
            throw;
        }
    }

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out IPcm? sound)
    {
        int sampleRate = AudioInfo.SampleRate;

        // SampleRate(1秒分)を超える場合はリクエストを分割
        if (length > sampleRate)
        {
            return ReadAudioChunked(start, length, sampleRate, out sound);
        }

        return ReadAudioCore(start, length, out sound);
    }

    private unsafe bool ReadAudioCore(int start, int length, [NotNullWhen(true)] out IPcm? sound)
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

        var pcm = new Pcm<Stereo32BitFloat>(response.SampleRate, response.NumSamples);
        try
        {
            _audioBuffer!.Read(new Span<byte>((void*)pcm.Data, response.DataLength));
            sound = pcm;
            return true;
        }
        catch
        {
            pcm.Dispose();
            throw;
        }
    }

    private bool ReadAudioChunked(int start, int length, int chunkSize, [NotNullWhen(true)] out IPcm? sound)
    {
        var result = new Pcm<Stereo32BitFloat>(AudioInfo.SampleRate, length);
        try
        {
            int offset = 0;
            while (offset < length)
            {
                int currentChunk = Math.Min(chunkSize, length - offset);

                if (!ReadAudioCore(start + offset, currentChunk, out IPcm? chunkSound))
                {
                    result.Dispose();
                    sound = null;
                    return false;
                }

                using var chunk = (Pcm<Stereo32BitFloat>)chunkSound;

                // チャンクデータをコピー
                chunk.DataSpan.CopyTo(result.DataSpan.Slice(offset, chunk.NumSamples));
                offset += chunk.NumSamples;
            }

            sound = result;
            return true;
        }
        catch
        {
            result?.Dispose();
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                // fire-and-forget: UIスレッドからの呼び出しでデッドロックしないよう
                // 同期ブロックを避けて非同期で送信
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _connection.SendAndReceiveAsync(
                            IpcMessage.Create(_connection.NextId(), MessageType.CloseReader,
                                new CloseReaderRequest { ReaderId = _readerId }));
                    }
                    catch
                    {

                    }
                });
            }
            catch { }

            _videoBuffer?.Dispose();
            _audioBuffer?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void EnsureVideoBuffer(ReadVideoResponse response, string? newShmName)
    {
        bool nameChanged = newShmName != null;

        // リングバッファの場合: 全体サイズで確保
        long requiredCapacity = _ringSlotCount > 0
            ? _ringSlotSize * _ringSlotCount
            : response.DataLength;

        if (!nameChanged && _videoBuffer != null && _videoBuffer.Capacity >= requiredCapacity)
            return;

        _videoBuffer?.Dispose();
        string shmName = newShmName
            ?? _openResponse.VideoSharedMemoryName
            ?? throw new InvalidOperationException("Video shared memory name not provided");
        _videoBuffer = SharedMemoryBuffer.Open(shmName, requiredCapacity);
    }

    private void EnsureAudioBuffer(int requiredSize, string? newShmName)
    {
        bool nameChanged = newShmName != null;
        if (!nameChanged && _audioBuffer != null && _audioBuffer.Capacity >= requiredSize)
            return;

        _audioBuffer?.Dispose();
        string shmName = newShmName
            ?? _openResponse.AudioSharedMemoryName
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
            G = transferFn[0], A = transferFn[1], B = transferFn[2], C = transferFn[3],
            D = transferFn[4], E = transferFn[5], F = transferFn[6]
        };
        var xyz = BitmapColorSpaceXyz.Create(toXyzD50);

        return BitmapColorSpace.CreateRgb(fn, xyz);
    }
}
