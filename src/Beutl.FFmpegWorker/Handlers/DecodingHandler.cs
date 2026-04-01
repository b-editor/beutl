
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegWorker.Decoding;
using Beutl.Media.Decoding;

namespace Beutl.FFmpegWorker.Handlers;

internal sealed class DecodingHandler : IDisposable
{
    private readonly Dictionary<int, ReaderState> _readers = [];
    private int _nextReaderId;
    private int _shmGeneration;

    private sealed class ReaderState : IDisposable
    {
        public required FFmpegReader Reader { get; init; }
        public SharedMemoryBuffer? VideoBuffer { get; set; }
        public SharedMemoryBuffer? AudioBuffer { get; set; }

        public void Dispose()
        {
            Reader.Dispose();
            VideoBuffer?.Dispose();
            AudioBuffer?.Dispose();
        }
    }

    public IpcMessage HandleOpen(IpcMessage msg)
    {
        var request = msg.GetPayload<OpenFileRequest>()!;
        var settings = new FFmpegDecodingSettings();
        settings.ThreadCount = request.ThreadCount;
        settings.Acceleration = (FFmpegDecodingSettings.AccelerationOptions)request.Acceleration;
        settings.ForceSrgbGamma = request.ForceSrgbGamma;

        var options = new MediaOptions((MediaMode)request.StreamsToLoad);
        var reader = new FFmpegReader(request.FilePath, options, settings);
        int id = Interlocked.Increment(ref _nextReaderId);

        var state = new ReaderState { Reader = reader };

        // 共有メモリ作成
        if (reader.HasVideo)
        {
            int videoWidth = reader.VideoInfo.FrameSize.Width;
            int videoHeight = reader.VideoInfo.FrameSize.Height;
            long videoBufferSize = (long)videoWidth * videoHeight * 8 + 64; // RGBA64LE max
            string videoShmName = $"beutl-ffmpeg-video-{Environment.ProcessId}-{id}";
            state.VideoBuffer = SharedMemoryBuffer.Create(videoShmName, videoBufferSize);
        }

        if (reader.HasAudio)
        {
            long audioBufferSize = reader.AudioInfo.SampleRate * 8 + 64; // 1 second stereo float
            string audioShmName = $"beutl-ffmpeg-audio-{Environment.ProcessId}-{id}";
            state.AudioBuffer = SharedMemoryBuffer.Create(audioShmName, audioBufferSize);
        }

        _readers[id] = state;

        var response = new OpenFileResponse
        {
            ReaderId = id,
            HasVideo = reader.HasVideo,
            HasAudio = reader.HasAudio,
            VideoSharedMemoryName = state.VideoBuffer?.Name,
            AudioSharedMemoryName = state.AudioBuffer?.Name,
        };

        if (reader.HasVideo)
        {
            var vi = reader.VideoInfo;
            response.VideoCodecName = vi.CodecName;
            response.VideoNumFrames = vi.NumFrames;
            response.VideoWidth = vi.FrameSize.Width;
            response.VideoHeight = vi.FrameSize.Height;
            response.FrameRateNum = vi.FrameRate.Numerator;
            response.FrameRateDen = vi.FrameRate.Denominator;
            response.DurationNum = vi.Duration.Numerator;
            response.DurationDen = vi.Duration.Denominator;
        }

        if (reader.HasAudio)
        {
            var ai = reader.AudioInfo;
            response.AudioCodecName = ai.CodecName;
            response.AudioDurationNum = ai.Duration.Numerator;
            response.AudioDurationDen = ai.Duration.Denominator;
            response.AudioSampleRate = ai.SampleRate;
            response.AudioNumChannels = ai.NumChannels;
        }

        return IpcMessage.Create(msg.Id, MessageType.OpenFileResult, response);
    }

    public unsafe IpcMessage HandleReadVideo(IpcMessage msg)
    {
        var request = msg.GetPayload<ReadVideoRequest>()!;
        if (!_readers.TryGetValue(request.ReaderId, out var state))
            return IpcMessage.CreateError(msg.Id, $"Unknown reader ID: {request.ReaderId}");

        if (!state.Reader.ReadVideo(request.Frame, out var bitmap))
        {
            return IpcMessage.Create(msg.Id, MessageType.ReadVideoResult,
                new ReadVideoResponse { Success = false });
        }

        using (bitmap)
        {
            int dataLen = bitmap.ByteCount;

            // 共有メモリが小さければリサイズ（一意な名前で再作成）
            string? newShmName = null;
            if (state.VideoBuffer == null || state.VideoBuffer.Capacity < dataLen)
            {
                state.VideoBuffer?.Dispose();
                long newSize = dataLen + 64;
                int gen = Interlocked.Increment(ref _shmGeneration);
                newShmName = $"beutl-ffmpeg-video-{Environment.ProcessId}-{request.ReaderId}-{gen}";
                state.VideoBuffer = SharedMemoryBuffer.Create(newShmName, newSize);
            }

            state.VideoBuffer.Write(new ReadOnlySpan<byte>((void*)bitmap.Data, dataLen));

            // 色空間情報シリアライズ
            var cs = bitmap.ColorSpace;
            var transferFn = cs.GetNumericalTransferFunction();
            var xyz = cs.ToColorSpaceXyz();

            return IpcMessage.Create(msg.Id, MessageType.ReadVideoResult, new ReadVideoResponse
            {
                Success = true,
                Width = bitmap.Width,
                Height = bitmap.Height,
                BytesPerPixel = bitmap.BytesPerPixel,
                DataLength = dataLen,
                IsHdr = bitmap.BytesPerPixel == 8,
                SharedMemoryName = newShmName,
                TransferFn = [transferFn.G, transferFn.A, transferFn.B, transferFn.C, transferFn.D, transferFn.E, transferFn.F],
                ToXyzD50 = xyz.Values.ToArray(),
            });
        }
    }

    public unsafe IpcMessage HandleReadAudio(IpcMessage msg)
    {
        var request = msg.GetPayload<ReadAudioRequest>()!;
        if (!_readers.TryGetValue(request.ReaderId, out var state))
            return IpcMessage.CreateError(msg.Id, $"Unknown reader ID: {request.ReaderId}");

        if (!state.Reader.ReadAudio(request.Start, request.Length, out var sound))
        {
            return IpcMessage.Create(msg.Id, MessageType.ReadAudioResult,
                new ReadAudioResponse { Success = false });
        }

        using (sound)
        {
            int dataLen = sound.NumSamples * (int)sound.SampleSize;

            string? newShmName = null;
            if (state.AudioBuffer == null || state.AudioBuffer.Capacity < dataLen)
            {
                state.AudioBuffer?.Dispose();
                long newSize = dataLen + 64;
                int gen = Interlocked.Increment(ref _shmGeneration);
                newShmName = $"beutl-ffmpeg-audio-{Environment.ProcessId}-{request.ReaderId}-{gen}";
                state.AudioBuffer = SharedMemoryBuffer.Create(newShmName, newSize);
            }

            state.AudioBuffer.Write(new ReadOnlySpan<byte>((void*)sound.Data, dataLen));

            return IpcMessage.Create(msg.Id, MessageType.ReadAudioResult, new ReadAudioResponse
            {
                Success = true,
                SampleRate = sound.SampleRate,
                NumSamples = sound.NumSamples,
                DataLength = dataLen,
                SharedMemoryName = newShmName,
            });
        }
    }

    public IpcMessage HandleClose(IpcMessage msg)
    {
        var request = msg.GetPayload<CloseReaderRequest>()!;
        if (_readers.Remove(request.ReaderId, out var state))
        {
            state.Dispose();
        }

        return IpcMessage.CreateSimple(msg.Id, MessageType.CloseReaderResult);
    }

    public IpcMessage HandleUpdateDecoderSettings(IpcMessage msg)
    {
        var request = msg.GetPayload<UpdateDecoderSettingsRequest>()!;

        foreach (KeyValuePair<int, ReaderState> kvp in _readers)
        {
            kvp.Value.Reader.Settings.ThreadCount = request.ThreadCount;
            kvp.Value.Reader.Settings.Acceleration = (FFmpegDecodingSettings.AccelerationOptions)request.Acceleration;
            kvp.Value.Reader.Settings.ForceSrgbGamma = request.ForceSrgbGamma;
        }

        return IpcMessage.CreateSimple(msg.Id, MessageType.UpdateDecoderSettingsResult);
    }

    public void Dispose()
    {
        foreach (var state in _readers.Values)
        {
            state.Dispose();
        }
        _readers.Clear();
    }
}
