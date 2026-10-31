
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
        var request = msg.RequirePayload<OpenFileRequest>(MessageType.OpenFile);
        var settings = new FFmpegDecodingSettings();
        ApplySettings(settings, request.ThreadCount, request.Acceleration);

        var options = new MediaOptions((MediaMode)request.StreamsToLoad);
        var reader = new FFmpegReader(request.FilePath, options, settings);
        int id = Interlocked.Increment(ref _nextReaderId);

        var state = new ReaderState { Reader = reader };
        try
        {
            var response = CreateReaderBuffers(id, state);
            _readers[id] = state;
            return IpcMessage.Create(msg.Id, MessageType.OpenFileResult, response);
        }
        catch
        {
            state.Dispose();
            throw;
        }
    }

    private OpenFileResponse CreateReaderBuffers(int id, ReaderState state)
    {
        var reader = state.Reader;
        int slotCount = DefaultSlotCount;
        long slotSize = 0;
        string? videoShmName = null;

        // 共有メモリ作成（リングバッファ）
        if (reader.HasVideo)
        {
            int videoWidth = reader.VideoInfo.FrameSize.Width;
            int videoHeight = reader.VideoInfo.FrameSize.Height;
            slotSize = FFmpegVideoSlotSizing.GetSlotSize(videoWidth, videoHeight, reader.IsHdr);

            long totalSize = slotSize * slotCount;
            videoShmName = $"beutl-ffmpeg-video-{Environment.ProcessId}-{id}";
            var videoBuffer = SharedMemoryBuffer.Create(videoShmName, totalSize);
            try
            {
                state.RingBuffer = new VideoRingBuffer(
                    slotCount, slotSize, videoBuffer,
                    reader, id, state.ReaderLock,
                    () => Interlocked.Increment(ref _shmGeneration));
            }
            catch
            {
                // Not yet owned by the state, so ReaderState.Dispose would not release it.
                videoBuffer.Dispose();
                throw;
            }
        }

        if (reader.HasAudio)
        {
            long audioBufferSize = (long)reader.AudioInfo.SampleRate * reader.AudioInfo.ChannelLayout.ChannelCount * sizeof(float) + 64;
            string audioShmName = $"beutl-ffmpeg-audio-{Environment.ProcessId}-{id}";
            state.AudioBuffer = SharedMemoryBuffer.Create(audioShmName, audioBufferSize);
        }

        return CreateOpenResponse(id, reader, state, videoShmName, slotCount, slotSize);
    }

    private static OpenFileResponse CreateOpenResponse(
        int id, FFmpegReader reader, ReaderState state, string? videoShmName, int slotCount, long slotSize)
    {
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
            response.AudioSpeakerMask = (uint)ai.ChannelLayout.Speakers;
        }

        return response;
    }

    // Test-only hook (null and inert in production): runs with the reader ID after an operation looked the reader up
    // and before it began using it, the window in which a parallel CloseReader can retire the reader.
    internal Action<int>? ReaderLookedUpForTest { get; set; }

    // Test-only hook (null and inert in production): runs with the reader ID once a ReadVideo holds its use of the
    // reader, so a test can keep the use, or fail the read, while a close waits for it.
    internal Action<int>? ReadVideoInUseForTest { get; set; }

    // A close removes the reader before retiring it, so an operation that looked the reader up just before can still
    // be about to use it. A use holds the close off until it ends; once the close has begun, the operation answers as
    // for an unknown reader instead of using one that is being disposed.
    private ReaderUse BeginReaderUse(int readerId)
    {
        if (!_readers.TryGetValue(readerId, out var state))
            return default;

        ReaderLookedUpForTest?.Invoke(readerId);
        return state.TryBeginUse() ? new ReaderUse(state) : default;
    }

    public IpcMessage HandleReadVideo(IpcMessage msg)
    {
        var request = msg.RequirePayload<ReadVideoRequest>(MessageType.ReadVideo);
        using ReaderUse use = BeginReaderUse(request.ReaderId);
        if (use.State is not { } state)
            return IpcMessage.CreateError(msg.Id, $"Unknown reader ID: {request.ReaderId}");

        ReadVideoInUseForTest?.Invoke(request.ReaderId);

        if (state.RingBuffer != null)
            return HandleReadVideoRingBuffer(msg.Id, request.Frame, state.RingBuffer);

        // HandleOpen gives every reader with a video stream a ring buffer, so only an audio-only reader gets here.
        return IpcMessage.CreateError(
            msg.Id,
            $"ReadVideo: reader {request.ReaderId} is audio-only and cannot satisfy a ReadVideo request.");
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
            SharedMemoryName = result.NewSharedMemoryName,
            RingBufferSlotSize = result.NewSharedMemoryName != null ? result.NewSlotSize : null,
            RingBufferSlotCount = result.NewSharedMemoryName != null ? result.NewSlotCount : null,
            SlotDataOffset = result.SlotDataOffset,
            TransferFn = result.TransferFn,
            ToXyzD50 = result.ToXyzD50,
        });
    }

    public unsafe IpcMessage HandleReadAudio(IpcMessage msg)
    {
        var request = msg.RequirePayload<ReadAudioRequest>(MessageType.ReadAudio);
        using ReaderUse use = BeginReaderUse(request.ReaderId);
        if (use.State is not { } state)
            return IpcMessage.CreateError(msg.Id, $"Unknown reader ID: {request.ReaderId}");

        state.ReaderLock.Wait();
        try
        {
            // Decode packed float samples directly into the shared memory allocated at open.
            int channels = state.Reader.HasAudio ? state.Reader.AudioInfo.ChannelLayout.ChannelCount : 0;
            long estimatedSize = (long)request.Length * channels * sizeof(float) + 64;

            // The host splits requests into one-second chunks, which fit the buffer created at open.
            if (state.AudioBuffer == null || state.AudioBuffer.Capacity < estimatedSize)
            {
                return IpcMessage.Create(msg.Id, MessageType.ReadAudioResult,
                    new ReadAudioResponse { Success = false });
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
                    NumChannels = audioInfo.NumChannels,
                    NumSamples = audioInfo.NumSamples,
                    DataLength = audioInfo.DataLength,
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
        var request = msg.RequirePayload<CloseReaderRequest>(MessageType.CloseReader);
        if (_readers.TryRemove(request.ReaderId, out var state))
        {
            RetireReader(state);
        }

        return IpcMessage.CreateSimple(msg.Id, MessageType.CloseReaderResult);
    }

    public void Dispose()
    {
        foreach (var readerId in _readers.Keys)
        {
            if (_readers.TryRemove(readerId, out var state))
            {
                RetireReader(state);
            }
        }
    }

    private static void RetireReader(ReaderState state)
    {
        // TryRemove keeps new operations from finding the reader, but one that found it just before may still be
        // running or about to start. Wait for those in progress; later ones answer as for an unknown reader.
        state.StopUses();
        // With no operation left to restart it, stop the prefetch, the last user of ReaderLock, and dispose.
        state.RingBuffer?.StopPrefetch();
        state.Dispose();
    }

    private static void ApplySettings(FFmpegDecodingSettings settings, int threadCount, int acceleration)
    {
        settings.ThreadCount.Value = threadCount;
        settings.Acceleration.Value = (FFmpegDecodingSettings.AccelerationOptions)acceleration;
    }
}
