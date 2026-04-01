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

            // 共有メモリ作成
            long videoBufferSize = (long)request.SourceWidth * request.SourceHeight * 8 + 64;
            long audioBufferSize = request.ProviderSampleRate * 8 + 64;

            string videoShmName = $"beutl-ffmpeg-encode-video-{Environment.ProcessId}-{msg.Id}";
            string audioShmName = $"beutl-ffmpeg-encode-audio-{Environment.ProcessId}-{msg.Id}";

            using var videoBuffer = SharedMemoryBuffer.Create(videoShmName, videoBufferSize);
            using var audioBuffer = SharedMemoryBuffer.Create(audioShmName, audioBufferSize);

            // 共有メモリ作成完了後にACK送信（名前とサイズを含む）
            await connection.SendAsync(IpcMessage.Create(msg.Id, MessageType.StartEncodeAck,
                new EncodeStartAckMessage
                {
                    VideoSharedMemoryName = videoShmName,
                    AudioSharedMemoryName = audioShmName,
                    VideoBufferSize = videoBufferSize,
                    AudioBufferSize = audioBufferSize,
                }));

            var frameProvider = new IpcFrameProvider(connection, videoBuffer,
                request.FrameCount, new Rational(request.FrameRateNum, request.FrameRateDen),
                request.IsHdr);
            var sampleProvider = new IpcSampleProvider(connection, audioBuffer,
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
