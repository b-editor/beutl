using System.Buffers;
using System.Runtime.InteropServices;
using Beutl.Audio;
using Beutl.Extensibility;
using Beutl.FFmpegIpc;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegIpc.Transport;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

namespace Beutl.Extensions.FFmpeg.Encoding;

public class FFmpegEncodingControllerProxy(string outputFile, FFmpegEncodingSettings settings)
    : EncodingController(outputFile)
{
    private readonly ILogger _logger = Log.CreateLogger<FFmpegEncodingControllerProxy>();

    public override FFmpegVideoEncoderSettings VideoSettings { get; } = new() { OutputFile = outputFile };

    public override FFmpegAudioEncoderSettings AudioSettings { get; } = new() { OutputFile = outputFile };

    public override async ValueTask Encode(
        IFrameProvider frameProvider, ISampleProvider sampleProvider, CancellationToken cancellationToken)
    {
        using var worker = FFmpegWorkerProcess.CreateForEncoding();
        IpcConnection connection;
        try
        {
            connection = await worker.EnsureStartedAsync(cancellationToken);
        }
        catch (FFmpegLibrariesNotFoundException)
        {
            FFmpegLibraryState.NotifyMissing();
            throw;
        }

        // エンコード設定をシリアライズ
        EncodeStartRequest startRequest = CreateStartRequest(frameProvider, sampleProvider);

        // StartEncode送信
        var startMsg = IpcMessage.Create(connection.NextId(), MessageType.StartEncode, startRequest);
        var ack = await connection.SendAndReceiveAsync(startMsg, cancellationToken);

        var ackPayload = ack.GetPayload<EncodeStartAckMessage>()
            ?? throw new InvalidOperationException("StartEncodeAck missing payload");

        // 共有メモリオープン (ダブルバッファリング: Worker側で作成済み、名前はACKから取得)
        using var videoBuffer0 = SharedMemoryBuffer.Open(ackPayload.VideoSharedMemoryName, ackPayload.VideoBufferSize);
        using var videoBuffer1 = SharedMemoryBuffer.Open(ackPayload.VideoSharedMemoryName2, ackPayload.VideoBufferSize);
        using var audioBuffer0 = SharedMemoryBuffer.Open(ackPayload.AudioSharedMemoryName, ackPayload.AudioBufferSize);
        using var audioBuffer1 = SharedMemoryBuffer.Open(ackPayload.AudioSharedMemoryName2, ackPayload.AudioBufferSize);

        SharedMemoryBuffer[] videoBuffers = [videoBuffer0, videoBuffer1];
        SharedMemoryBuffer[] audioBuffers = [audioBuffer0, audioBuffer1];
        long nextSample = 0;

        // メッセージループ: Workerからの要求に応答
        long nextFrame = 0;
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
                            var frameReq = msg.GetPayload<RequestFrameMessage>()
                                ?? throw new InvalidOperationException("Missing payload for RequestFrame");
                            if (frameReq.BufferIndex is not (0 or 1))
                                throw new InvalidOperationException($"Invalid BufferIndex: {frameReq.BufferIndex}");
                            if (frameReq.FrameIndex != nextFrame)
                                throw new InvalidOperationException(
                                    $"The worker asked for frame {frameReq.FrameIndex}, but the next frame is {nextFrame}.");
                            using var bitmap = await frameProvider.ReadNextFrame()
                                ?? throw new InvalidOperationException($"The frame provider ended before frame {nextFrame}.");
                            nextFrame++;

                            // BufferIndex で指定されたバッファに書き込み (ダブルバッファリング)
                            var targetVideoBuffer = videoBuffers[frameReq.BufferIndex];
                            unsafe
                            {
                                targetVideoBuffer.Write(new ReadOnlySpan<byte>((void*)bitmap.Data, bitmap.ByteCount));
                            }

                            await connection.SendAsync(IpcMessage.Create(msg.Id, MessageType.ProvideFrame,
                                new ProvideFrameMessage
                                {
                                    Width = bitmap.Width,
                                    Height = bitmap.Height,
                                    DataLength = bitmap.ByteCount,
                                    Premul = bitmap.AlphaType == BitmapAlphaType.Premul,
                                    ColorType = (int)bitmap.ColorType
                                }), cancellationToken);
                            break;
                        }

                    case MessageType.RequestSample:
                        {
                            var sampleReq = msg.GetPayload<RequestSampleMessage>()
                                ?? throw new InvalidOperationException("Missing payload for RequestSample");
                            if (sampleReq.BufferIndex is not (0 or 1))
                                throw new InvalidOperationException($"Invalid BufferIndex: {sampleReq.BufferIndex}");
                            if (sampleReq.Offset != nextSample)
                                throw new InvalidOperationException(
                                    $"The worker asked for samples from {sampleReq.Offset}, but the next sample is {nextSample}.");
                            using var audio = await sampleProvider.ReadNextSamples(checked((int)sampleReq.Length))
                                ?? throw new InvalidOperationException($"The sample provider ended before sample {nextSample}.");
                            nextSample += sampleReq.Length;

                            // BufferIndex で指定されたバッファに書き込み (ダブルバッファリング)
                            var targetAudioBuffer = audioBuffers[sampleReq.BufferIndex];
                            int length = audio.SampleCount * 2;
                            float[] interleaved = ArrayPool<float>.Shared.Rent(length);
                            try
                            {
                                AudioConversion.Interleave(audio, interleaved);
                                targetAudioBuffer.Write(MemoryMarshal.AsBytes(interleaved.AsSpan(0, length)));
                            }
                            finally
                            {
                                ArrayPool<float>.Shared.Return(interleaved);
                            }

                            await connection.SendAsync(IpcMessage.Create(msg.Id, MessageType.ProvideSample,
                                new ProvideSampleMessage
                                {
                                    NumSamples = audio.SampleCount,
                                    DataLength = length * sizeof(float),
                                }), cancellationToken);
                            break;
                        }

                    case MessageType.EncodeComplete:
                        ThrowIfEncodeFailed(msg);
                        return;

                    case MessageType.Error:
                        throw new FFmpegWorkerException(msg.Error ?? "Unknown error", msg.ErrorStackTrace, msg.ErrorCode);

                    default:
                        break;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            // キャンセル時にWorkerへ通知
            await NotifyWorkerCanceledAsync(connection);

            throw;
        }
    }

    private EncodeStartRequest CreateStartRequest(IFrameProvider frameProvider, ISampleProvider sampleProvider)
    {
        return new EncodeStartRequest
        {
            OutputFile = OutputFile,
            FrameCount = frameProvider.FrameCount,
            FrameRateNum = frameProvider.FrameRate.Numerator,
            FrameRateDen = frameProvider.FrameRate.Denominator,
            SampleCount = sampleProvider.SampleCount,
            ProviderSampleRate = sampleProvider.SampleRate,
            SourceWidth = VideoSettings.SourceSize.Value.Width,
            SourceHeight = VideoSettings.SourceSize.Value.Height,
            DestWidth = VideoSettings.DestinationSize.Value.Width,
            DestHeight = VideoSettings.DestinationSize.Value.Height,
            VideoBitrate = VideoSettings.Bitrate.Value,
            KeyframeRate = VideoSettings.KeyframeRate.Value,
            PixelFormat = VideoSettings.Format.Value,
            VideoCodecName = VideoSettings.Codec.Value.Name,
            ColorPrimaries = VideoSettings.ColorPrimaries.Value,
            ColorTrc = VideoSettings.ColorTrc.Value,
            ColorSpace = VideoSettings.ColorSpace.Value,
            ColorRange = VideoSettings.ColorRange.Value,
            VideoOptions = VideoSettings.Options.Value
                .Where(o => !string.IsNullOrWhiteSpace(o.Name.Value))
                .ToDictionary(o => o.Name.Value, o => o.Value.Value),
            AudioSampleRate = AudioSettings.SampleRate.Value,
            AudioChannels = AudioSettings.Channels.Value,
            AudioBitrate = AudioSettings.Bitrate.Value,
            AudioFormat = (int)AudioSettings.Format.Value,
            AudioCodecName = AudioSettings.Codec.Value.Name,
            ThreadCount = settings.ThreadCount.Value,
            Acceleration = (int)settings.Acceleration.Value,
        };
    }

    private static void ThrowIfEncodeFailed(IpcMessage msg)
    {
        var complete = msg.GetPayload<EncodeCompleteMessage>();
        if (complete != null && !complete.Success)
        {
            throw new FFmpegWorkerException(
                complete.Error ?? "Encoding failed",
                ffmpegErrorCode: complete.FFmpegErrorCode);
        }
    }

    private async ValueTask NotifyWorkerCanceledAsync(IpcConnection connection)
    {
        try
        {
            await connection.SendAsync(
                IpcMessage.CreateSimple(connection.NextId(), MessageType.CancelEncode),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send cancel notification to FFmpeg worker");
        }
    }
}
