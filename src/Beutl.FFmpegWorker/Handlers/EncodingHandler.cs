using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegIpc.Transport;
using Beutl.FFmpegWorker.Encoding;
using Beutl.FFmpegWorker.Providers;
using Beutl.Media;

namespace Beutl.FFmpegWorker.Handlers;

internal sealed class EncodingHandler : IDisposable
{
    private CancellationTokenSource? _encodeCts;

    public async Task<IpcMessage> HandleStartAsync(IpcMessage msg, IpcConnection connection, CancellationToken ct)
    {
        var request = msg.GetPayload<EncodeStartRequest>()!;

        _encodeCts?.Dispose();
        _encodeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            var encodingSettings = new FFmpegEncodingSettings();
            encodingSettings.Acceleration = (FFmpegEncodingSettings.AccelerationOptions)request.Acceleration;
            encodingSettings.ThreadCount = request.ThreadCount;

            var controller = new FFmpegEncodingController(request.OutputFile, encodingSettings);

            // Video settings
            controller.VideoSettings.SourceSize = new PixelSize(request.SourceWidth, request.SourceHeight);
            controller.VideoSettings.DestinationSize = new PixelSize(request.DestWidth, request.DestHeight);
            controller.VideoSettings.FrameRate = new Rational(request.FrameRateNum, request.FrameRateDen);
            controller.VideoSettings.Bitrate = request.VideoBitrate;
            controller.VideoSettings.KeyframeRate = request.KeyframeRate;
            controller.VideoSettings.Format = request.PixelFormat;
            controller.VideoSettings.Codec = request.VideoCodecName == "Default"
                ? CodecRecord.Default
                : new CodecRecord(request.VideoCodecName, request.VideoCodecName);
            controller.VideoSettings.ColorPrimaries = request.ColorPrimaries;
            controller.VideoSettings.ColorTrc = request.ColorTrc;
            controller.VideoSettings.ColorSpace = request.ColorSpace;
            controller.VideoSettings.ColorRange = request.ColorRange;

            // Video options
            controller.VideoSettings.Options.Clear();
            foreach (var kvp in request.VideoOptions)
            {
                controller.VideoSettings.Options.Add(new AdditionalOption(kvp.Key, kvp.Value));
            }

            // Audio settings
            controller.AudioSettings.SampleRate = request.AudioSampleRate;
            controller.AudioSettings.Channels = request.AudioChannels;
            controller.AudioSettings.Bitrate = request.AudioBitrate;
            controller.AudioSettings.Format = (FFmpegAudioEncoderSettings.AudioFormat)request.AudioFormat;
            controller.AudioSettings.Codec = request.AudioCodecName == "Default"
                ? CodecRecord.Default
                : new CodecRecord(request.AudioCodecName, request.AudioCodecName);

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

            var frameProvider = new IpcFrameProvider(connection, videoBuffers,
                request.FrameCount, new Rational(request.FrameRateNum, request.FrameRateDen));
            var sampleProvider = new IpcSampleProvider(connection, audioBuffers,
                request.SampleCount, request.ProviderSampleRate);

            await controller.Encode(frameProvider, sampleProvider, _encodeCts.Token);

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
            Console.WriteLine(ex);
            return IpcMessage.Create(msg.Id, MessageType.EncodeComplete,
                new EncodeCompleteMessage { Success = false, Error = ex.Message });
        }
    }

    public IpcMessage HandleCancel(IpcMessage msg)
    {
        _encodeCts?.Cancel();
        return IpcMessage.Create(msg.Id, MessageType.EncodeComplete,
            new EncodeCompleteMessage { Success = false, Error = "Cancelled" });
    }

    public void Dispose()
    {
        _encodeCts?.Dispose();
    }
}
