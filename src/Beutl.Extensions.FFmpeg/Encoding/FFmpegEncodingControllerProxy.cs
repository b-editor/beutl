using Beutl.Extensibility;
using Beutl.FFmpegIpc;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.Media;

namespace Beutl.Extensions.FFmpeg.Encoding;

public class FFmpegEncodingControllerProxy(string outputFile, FFmpegEncodingSettings settings)
    : EncodingController(outputFile)
{
    public override FFmpegVideoEncoderSettings VideoSettings { get; } = new() { OutputFile = outputFile };

    public override FFmpegAudioEncoderSettings AudioSettings { get; } = new() { OutputFile = outputFile };

    public override async ValueTask Encode(
        IFrameProvider frameProvider, ISampleProvider sampleProvider, CancellationToken cancellationToken)
    {
        var connection = await FFmpegWorkerProcess.EncodingInstance.EnsureStartedAsync(cancellationToken);

        // エンコード設定をシリアライズ
        var startRequest = new EncodeStartRequest
        {
            OutputFile = OutputFile,
            FrameCount = frameProvider.FrameCount,
            FrameRateNum = frameProvider.FrameRate.Numerator,
            FrameRateDen = frameProvider.FrameRate.Denominator,
            SampleCount = sampleProvider.SampleCount,
            ProviderSampleRate = sampleProvider.SampleRate,
            SourceWidth = VideoSettings.SourceSize.Width,
            SourceHeight = VideoSettings.SourceSize.Height,
            DestWidth = VideoSettings.DestinationSize.Width,
            DestHeight = VideoSettings.DestinationSize.Height,
            VideoBitrate = VideoSettings.Bitrate,
            KeyframeRate = VideoSettings.KeyframeRate,
            PixelFormat = VideoSettings.Format,
            VideoCodecName = VideoSettings.Codec.Name,
            ColorPrimaries = VideoSettings.ColorPrimaries,
            ColorTrc = VideoSettings.ColorTrc,
            ColorSpace = VideoSettings.ColorSpace,
            ColorRange = VideoSettings.ColorRange,
            VideoOptions = VideoSettings.Options
                .Where(o => !string.IsNullOrWhiteSpace(o.Name))
                .ToDictionary(o => o.Name, o => o.Value),
            IsHdr = IsHdr(),
            AudioSampleRate = AudioSettings.SampleRate,
            AudioChannels = AudioSettings.Channels,
            AudioBitrate = AudioSettings.Bitrate,
            AudioFormat = (int)AudioSettings.Format,
            AudioCodecName = AudioSettings.Codec.Name,
            ThreadCount = settings.ThreadCount,
            Acceleration = (int)settings.Acceleration,
        };

        // StartEncode送信
        var startMsg = IpcMessage.Create(connection.NextId(), MessageType.StartEncode, startRequest);
        var ack = await connection.SendAndReceiveAsync(startMsg, cancellationToken)
                  ?? throw new IOException("Connection closed waiting for StartEncodeAck");

        if (ack.Error != null)
            throw new FFmpegWorkerException(ack.Error);

        var ackPayload = ack.GetPayload<EncodeStartAckMessage>()
            ?? throw new InvalidOperationException("StartEncodeAck missing payload");

        // 共有メモリオープン (Worker側で作成済み、名前はACKから取得)
        using var videoBuffer = SharedMemoryBuffer.Open(ackPayload.VideoSharedMemoryName, ackPayload.VideoBufferSize);
        using var audioBuffer = SharedMemoryBuffer.Open(ackPayload.AudioSharedMemoryName, ackPayload.AudioBufferSize);

        // メッセージループ: Workerからの要求に応答
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var msg = await connection.ReceiveAsync(cancellationToken);
                if (msg == null)
                    throw new IOException("Connection closed during encoding");

                switch (msg.Type)
                {
                    case MessageType.RequestFrame:
                    {
                        var frameReq = msg.GetPayload<RequestFrameMessage>()!;
                        using var bitmap = await frameProvider.RenderFrame(frameReq.FrameIndex);

                        // Linear sRGBのまま渡す (色空間変換はWorker側で行う)
                        unsafe
                        {
                            videoBuffer.Write(new ReadOnlySpan<byte>((void*)bitmap.Data, bitmap.ByteCount));
                        }

                        await connection.SendAsync(IpcMessage.Create(msg.Id, MessageType.ProvideFrame,
                            new ProvideFrameMessage
                            {
                                Width = bitmap.Width,
                                Height = bitmap.Height,
                                BytesPerPixel = bitmap.BytesPerPixel,
                                DataLength = bitmap.ByteCount,
                                Premul = bitmap.AlphaType == BitmapAlphaType.Premul
                            }), cancellationToken);
                        break;
                    }

                    case MessageType.RequestSample:
                    {
                        var sampleReq = msg.GetPayload<RequestSampleMessage>()!;
                        using var pcm = await sampleProvider.Sample(sampleReq.Offset, sampleReq.Length);

                        int dataLen;
                        unsafe
                        {
                            dataLen = pcm.NumSamples * (int)pcm.SampleSize;
                            audioBuffer.Write(new ReadOnlySpan<byte>((void*)pcm.Data, dataLen));
                        }

                        await connection.SendAsync(IpcMessage.Create(msg.Id, MessageType.ProvideSample,
                            new ProvideSampleMessage
                            {
                                NumSamples = pcm.NumSamples,
                                DataLength = dataLen,
                            }), cancellationToken);
                        break;
                    }

                    case MessageType.EncodeComplete:
                    {
                        var complete = msg.GetPayload<EncodeCompleteMessage>();
                        if (complete != null && !complete.Success)
                        {
                            throw new FFmpegWorkerException(complete.Error ?? "Encoding failed");
                        }
                        return;
                    }

                    case MessageType.EncodeProgress:
                        break;

                    case MessageType.Error:
                        throw new FFmpegWorkerException(msg.Error ?? "Unknown error", msg.ErrorStackTrace);

                    default:
                        break;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            // キャンセル時にWorkerへ通知
            try
            {
                await connection.SendAsync(
                    IpcMessage.CreateSimple(connection.NextId(), MessageType.CancelEncode),
                    CancellationToken.None);
            }
            catch { }

            throw;
        }
    }

    private bool IsHdr()
    {
        return VideoSettings.ColorTrc is FFColorTransfer.SMPTE2084 or FFColorTransfer.ARIB_STD_B67;
    }
}
