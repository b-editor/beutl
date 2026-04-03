
using System.Collections.Concurrent;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegWorker.Decoding;
using Beutl.Media.Decoding;

namespace Beutl.FFmpegWorker.Handlers;

internal sealed partial class DecodingHandler : IDisposable
{
    private const int DefaultSlotCount = 4;

    private readonly ConcurrentDictionary<int, ReaderState> _readers = [];
    private int _nextReaderId;
    private int _shmGeneration;

    public IpcMessage HandleOpen(IpcMessage msg)
    {
        var request = msg.GetPayload<OpenFileRequest>()
            ?? throw new InvalidOperationException("Missing payload for OpenFile");
        var settings = new FFmpegDecodingSettings();
        settings.ThreadCount = request.ThreadCount;
        settings.Acceleration = (FFmpegDecodingSettings.AccelerationOptions)request.Acceleration;
        settings.ForceSrgbGamma = request.ForceSrgbGamma;

        var options = new MediaOptions((MediaMode)request.StreamsToLoad);
        var reader = new FFmpegReader(request.FilePath, options, settings);
        int id = Interlocked.Increment(ref _nextReaderId);

        var state = new ReaderState { Reader = reader };

        int slotCount = DefaultSlotCount;
        long slotSize = 0;
        string? videoShmName = null;

        // 共有メモリ作成（リングバッファ）
        if (reader.HasVideo)
        {
            int videoWidth = reader.VideoInfo.FrameSize.Width;
            int videoHeight = reader.VideoInfo.FrameSize.Height;
            slotSize = (long)videoWidth * videoHeight * 8 + 64; // RGBA64LE max

            long totalSize = slotSize * slotCount;
            videoShmName = $"beutl-ffmpeg-video-{Environment.ProcessId}-{id}";
            var videoBuffer = SharedMemoryBuffer.Create(videoShmName, totalSize);

            state.RingBuffer = new VideoRingBuffer(
                slotCount, slotSize, videoBuffer,
                reader, id, state.ReaderLock,
                () => Interlocked.Increment(ref _shmGeneration));
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
            VideoSharedMemoryName = videoShmName,
            AudioSharedMemoryName = state.AudioBuffer?.Name,
            VideoRingBufferSlotCount = reader.HasVideo ? slotCount : 0,
            VideoRingBufferSlotSize = slotSize,
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

    public IpcMessage HandleReadVideo(IpcMessage msg)
    {
        var request = msg.GetPayload<ReadVideoRequest>()
            ?? throw new InvalidOperationException("Missing payload for ReadVideo");
        if (!_readers.TryGetValue(request.ReaderId, out var state))
            return IpcMessage.CreateError(msg.Id, $"Unknown reader ID: {request.ReaderId}");

        if (state.RingBuffer != null)
            return HandleReadVideoRingBuffer(msg.Id, request.Frame, state.RingBuffer);

        return HandleReadVideoLegacy(msg.Id, request, state);
    }

    private static IpcMessage HandleReadVideoRingBuffer(int msgId, int frame, VideoRingBuffer ringBuffer)
    {
        var result = ringBuffer.ReadFrame(frame);

        return IpcMessage.Create(msgId, MessageType.ReadVideoResult, new ReadVideoResponse
        {
            Success = result.Success,
            Width = result.Width,
            Height = result.Height,
            BytesPerPixel = result.BytesPerPixel,
            DataLength = result.DataLength,
            IsHdr = result.IsHdr,
            SharedMemoryName = result.NewSharedMemoryName,
            SlotIndex = result.Success ? result.SlotIndex : null,
            SlotDataOffset = result.SlotDataOffset,
            TransferFn = result.TransferFn,
            ToXyzD50 = result.ToXyzD50,
        });
    }

    private unsafe IpcMessage HandleReadVideoLegacy(int msgId, ReadVideoRequest request, ReaderState state)
    {
        state.ReaderLock.Wait();
        try
        {
            // 共有メモリが未作成の場合、まず確保
            string? newShmName = null;
            if (state.VideoBuffer == null)
            {
                int videoWidth = state.Reader.VideoInfo.FrameSize.Width;
                int videoHeight = state.Reader.VideoInfo.FrameSize.Height;
                long newSize = (long)videoWidth * videoHeight * 8 + 64;
                int gen = Interlocked.Increment(ref _shmGeneration);
                newShmName = $"beutl-ffmpeg-video-{Environment.ProcessId}-{request.ReaderId}-{gen}";
                state.VideoBuffer = SharedMemoryBuffer.Create(newShmName, newSize);
            }

            // 共有メモリに直接デコード
            byte* ptr = state.VideoBuffer.AcquirePointer();
            try
            {
                var destination = new Span<byte>(ptr, (int)state.VideoBuffer.Capacity);

                if (!state.Reader.ReadVideo(request.Frame, destination, out var frameInfo))
                {
                    // バッファが小さい場合リサイズして再試行
                    if (frameInfo.DataLength > 0 && frameInfo.DataLength > state.VideoBuffer.Capacity)
                    {
                        state.VideoBuffer.ReleasePointer();
                        ptr = null;
                        state.VideoBuffer.Dispose();
                        long newSize = frameInfo.DataLength + 64;
                        int gen = Interlocked.Increment(ref _shmGeneration);
                        newShmName = $"beutl-ffmpeg-video-{Environment.ProcessId}-{request.ReaderId}-{gen}";
                        state.VideoBuffer = SharedMemoryBuffer.Create(newShmName, newSize);

                        ptr = state.VideoBuffer.AcquirePointer();
                        destination = new Span<byte>(ptr, (int)state.VideoBuffer.Capacity);

                        if (!state.Reader.ReadVideo(request.Frame, destination, out frameInfo))
                        {
                            return IpcMessage.Create(msgId, MessageType.ReadVideoResult,
                                new ReadVideoResponse { Success = false, SharedMemoryName = newShmName });
                        }
                    }
                    else
                    {
                        return IpcMessage.Create(msgId, MessageType.ReadVideoResult,
                            new ReadVideoResponse { Success = false });
                    }
                }

                // 色空間情報: 前フレームと異なる場合のみ送信
                bool colorSpaceChanged = state.LastColorSpace != frameInfo.ColorSpace;
                state.LastColorSpace = frameInfo.ColorSpace;
                if (colorSpaceChanged || state.LastToXyzD50 == null || state.LastTransferFn == null)
                {
                    (state.LastTransferFn, state.LastToXyzD50) = ColorSpaceIpcHelper.Extract(frameInfo.ColorSpace);
                }

                return IpcMessage.Create(msgId, MessageType.ReadVideoResult, new ReadVideoResponse
                {
                    Success = true,
                    Width = frameInfo.Width,
                    Height = frameInfo.Height,
                    BytesPerPixel = frameInfo.BytesPerPixel,
                    DataLength = frameInfo.DataLength,
                    IsHdr = frameInfo.IsHdr,
                    SharedMemoryName = newShmName,
                    TransferFn = colorSpaceChanged ? state.LastTransferFn : null,
                    ToXyzD50 = colorSpaceChanged ? state.LastToXyzD50 : null,
                });
            }
            finally
            {
                if (ptr != null)
                    state.VideoBuffer!.ReleasePointer();
            }
        }
        finally
        {
            state.ReaderLock.Release();
        }
    }

    public unsafe IpcMessage HandleReadAudio(IpcMessage msg)
    {
        var request = msg.GetPayload<ReadAudioRequest>()
            ?? throw new InvalidOperationException("Missing payload for ReadAudio");
        if (!_readers.TryGetValue(request.ReaderId, out var state))
            return IpcMessage.CreateError(msg.Id, $"Unknown reader ID: {request.ReaderId}");

        state.ReaderLock.Wait();
        try
        {
            // 共有メモリに直接デコード
            // 必要なバッファサイズを推定（Stereo32BitFloat = 8 bytes per sample）
            long estimatedSize = (long)request.Length * 8 + 64;
            string? newShmName = null;

            if (state.AudioBuffer == null || state.AudioBuffer.Capacity < estimatedSize)
            {
                state.AudioBuffer?.Dispose();
                int gen = Interlocked.Increment(ref _shmGeneration);
                newShmName = $"beutl-ffmpeg-audio-{Environment.ProcessId}-{request.ReaderId}-{gen}";
                state.AudioBuffer = SharedMemoryBuffer.Create(newShmName, estimatedSize);
            }

            byte* ptr = state.AudioBuffer.AcquirePointer();
            try
            {
                var destination = new Span<byte>(ptr, (int)state.AudioBuffer.Capacity);

                if (!state.Reader.ReadAudio(request.Start, request.Length, destination, out var audioInfo))
                {
                    return IpcMessage.Create(msg.Id, MessageType.ReadAudioResult,
                        new ReadAudioResponse { Success = false });
                }

                return IpcMessage.Create(msg.Id, MessageType.ReadAudioResult, new ReadAudioResponse
                {
                    Success = true,
                    SampleRate = audioInfo.SampleRate,
                    NumSamples = audioInfo.NumSamples,
                    DataLength = audioInfo.DataLength,
                    SharedMemoryName = newShmName,
                });
            }
            finally
            {
                state.AudioBuffer!.ReleasePointer();
            }
        }
        finally
        {
            state.ReaderLock.Release();
        }
    }

    public IpcMessage HandleClose(IpcMessage msg)
    {
        var request = msg.GetPayload<CloseReaderRequest>()
            ?? throw new InvalidOperationException("Missing payload for CloseReader");
        if (_readers.TryRemove(request.ReaderId, out var state))
        {
            state.Dispose();
        }

        return IpcMessage.CreateSimple(msg.Id, MessageType.CloseReaderResult);
    }

    public IpcMessage HandleUpdateDecoderSettings(IpcMessage msg)
    {
        var request = msg.GetPayload<UpdateDecoderSettingsRequest>()
            ?? throw new InvalidOperationException("Missing payload for UpdateDecoderSettings");

        foreach (KeyValuePair<int, ReaderState> kvp in _readers)
        {
            var state = kvp.Value;

            // プリフェッチ停止、リングバッファ無効化
            if (state.RingBuffer != null)
            {
                state.RingBuffer.StopPrefetch();
                state.RingBuffer.InvalidateAllSlots();
            }

            state.Reader.Settings.ThreadCount = request.ThreadCount;
            state.Reader.Settings.Acceleration = (FFmpegDecodingSettings.AccelerationOptions)request.Acceleration;
            state.Reader.Settings.ForceSrgbGamma = request.ForceSrgbGamma;
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
