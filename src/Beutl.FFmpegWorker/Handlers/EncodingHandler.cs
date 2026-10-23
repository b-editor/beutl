using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.Providers;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegIpc.Transport;
using Beutl.FFmpegWorker.Encoding;
using Beutl.Media;

namespace Beutl.FFmpegWorker.Handlers;

internal sealed class EncodingHandler
{
    public async Task<IpcMessage> HandleStartAsync(IpcMessage msg, IpcConnection connection, CancellationToken ct)
    {
        var request = msg.RequirePayload<EncodeStartRequest>(MessageType.StartEncode);

        try
        {
            var controller = CreateController(request);

            // 共有メモリ作成 (ダブルバッファリング)
            long videoBufferSize = (long)request.SourceWidth * request.SourceHeight * 8 + 64;
            long audioBufferSize = request.ProviderSampleRate * 8 + 64;

            string videoShmName0 = $"beutl-ffmpeg-encode-video-{Environment.ProcessId}-{msg.Id}-0";
            string videoShmName1 = $"beutl-ffmpeg-encode-video-{Environment.ProcessId}-{msg.Id}-1";
            string audioShmName0 = $"beutl-ffmpeg-encode-audio-{Environment.ProcessId}-{msg.Id}-0";
            string audioShmName1 = $"beutl-ffmpeg-encode-audio-{Environment.ProcessId}-{msg.Id}-1";

            using var videoBuffer0 = SharedMemoryBuffer.Create(videoShmName0, videoBufferSize);
            using var videoBuffer1 = SharedMemoryBuffer.Create(videoShmName1, videoBufferSize);
            using var audioBuffer0 = SharedMemoryBuffer.Create(audioShmName0, audioBufferSize);
            using var audioBuffer1 = SharedMemoryBuffer.Create(audioShmName1, audioBufferSize);

            SharedMemoryBuffer[] videoBuffers = [videoBuffer0, videoBuffer1];
            SharedMemoryBuffer[] audioBuffers = [audioBuffer0, audioBuffer1];

            // 共有メモリ作成完了後にACK送信（名前とサイズを含む）
            await connection.SendAsync(IpcMessage.Create(msg.Id, MessageType.StartEncodeAck,
                new EncodeStartAckMessage
                {
                    VideoSharedMemoryName = videoShmName0,
                    AudioSharedMemoryName = audioShmName0,
                    VideoBufferSize = videoBufferSize,
                    AudioBufferSize = audioBufferSize,
                    VideoSharedMemoryName2 = videoShmName1,
                    AudioSharedMemoryName2 = audioShmName1,
                }));

            // Dispose the providers once Encode returns or throws so any in-flight prefetch is observed
            // (drained) and the cached chunk is freed — preventing a faulted background task from later
            // surfacing as an UnobservedTaskException.
            using var frameProvider = new IpcFrameProvider(connection, videoBuffers,
                request.FrameCount, new Rational(request.FrameRateNum, request.FrameRateDen),
                request.SourceWidth, request.SourceHeight);
            using var sampleProvider = new IpcSampleProvider(connection, audioBuffers,
                request.SampleCount, request.ProviderSampleRate);

            await controller.Encode(frameProvider, sampleProvider, ct);

            return IpcMessage.Create(msg.Id, MessageType.EncodeComplete,
                new EncodeCompleteMessage { Success = true });
        }
        catch (OperationCanceledException)
        {
            return IpcMessage.Create(msg.Id, MessageType.EncodeComplete,
                new EncodeCompleteMessage { Success = false, Error = "Cancelled" });
        }
        catch (Exception ex)
        {
            WorkerLog.Error("Encoding failed", ex);
            return IpcMessage.Create(msg.Id, MessageType.EncodeComplete,
                new EncodeCompleteMessage
                {
                    Success = false,
                    Error = ex.Message,
                    FFmpegErrorCode = FFmpegErrorCodeExtractor.TryGetFFmpegErrorCode(ex),
                });
        }
    }

    private static FFmpegEncodingController CreateController(EncodeStartRequest request)
    {
        var encodingSettings = new FFmpegEncodingSettings();
        encodingSettings.Acceleration.Value = (FFmpegEncodingSettings.AccelerationOptions)request.Acceleration;
        encodingSettings.ThreadCount.Value = request.ThreadCount;

        var controller = new FFmpegEncodingController(request.OutputFile, encodingSettings);

        // Video settings
        controller.VideoSettings.SourceSize.Value = new PixelSize(request.SourceWidth, request.SourceHeight);
        controller.VideoSettings.DestinationSize.Value = new PixelSize(request.DestWidth, request.DestHeight);
        controller.VideoSettings.FrameRate.Value = new Rational(request.FrameRateNum, request.FrameRateDen);
        controller.VideoSettings.Bitrate.Value = request.VideoBitrate;
        controller.VideoSettings.KeyframeRate.Value = request.KeyframeRate;
        controller.VideoSettings.Format.Value = request.PixelFormat;
        controller.VideoSettings.Codec.Value = ToCodecRecord(request.VideoCodecName);
        controller.VideoSettings.ColorPrimaries.Value = request.ColorPrimaries;
        controller.VideoSettings.ColorTrc.Value = request.ColorTrc;
        controller.VideoSettings.ColorSpace.Value = request.ColorSpace;
        controller.VideoSettings.ColorRange.Value = request.ColorRange;

        // Video options
        controller.VideoSettings.Options.Value.Clear();
        foreach (var kvp in request.VideoOptions)
        {
            controller.VideoSettings.Options.Value.Add(new AdditionalOption(kvp.Key, kvp.Value));
        }

        // Audio settings
        controller.AudioSettings.SampleRate.Value = request.AudioSampleRate;
        controller.AudioSettings.Channels.Value = request.AudioChannels;
        controller.AudioSettings.Bitrate.Value = request.AudioBitrate;
        controller.AudioSettings.Format.Value = (FFmpegAudioEncoderSettings.AudioFormat)request.AudioFormat;
        controller.AudioSettings.Codec.Value = ToCodecRecord(request.AudioCodecName);

        return controller;
    }

    private static CodecRecord ToCodecRecord(string codecName)
    {
        return codecName == "Default"
            ? CodecRecord.Default
            : new CodecRecord(codecName, codecName);
    }

    // StartEncode is handled sequentially, so this runs only after an encode has ended. A CancelEncode sent
    // during an encode is read by IpcConnection.SendAndReceiveAsync in place of a frame or sample reply.
    public IpcMessage HandleCancel(IpcMessage msg)
    {
        return IpcMessage.Create(msg.Id, MessageType.EncodeComplete,
            new EncodeCompleteMessage { Success = false, Error = "Cancelled" });
    }
}
