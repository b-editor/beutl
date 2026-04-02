using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Transport;
using Beutl.FFmpegWorker.Handlers;

namespace Beutl.FFmpegWorker;

internal sealed class WorkerHost(IpcConnection connection) : IDisposable
{
    private readonly DecodingHandler _decodingHandler = new();
    private readonly EncodingHandler _encodingHandler = new();
    private readonly CodecQueryHandler _codecQueryHandler = new();

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            IpcMessage? message;
            try
            {
                message = await connection.ReceiveAsync(ct);
            }
            catch (IOException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (message == null)
                break;

            // デコード系メッセージは並行処理可能（fire-and-forget で送信）
            if (IsParallelizable(message.Type))
            {
                var msg = message;
                _ = Task.Run(async () =>
                {
                    IpcMessage response;
                    try
                    {
                        response = HandleMessage(msg);
                    }
                    catch (Exception ex)
                    {
                        response = IpcMessage.CreateError(msg.Id, ex.Message, ex.StackTrace);
                    }

                    try
                    {
                        await connection.SendAsync(response, ct);
                    }
                    catch (IOException) { }
                }, ct);
                continue;
            }

            // エンコード系・ライフサイクル系は逐次処理
            IpcMessage seqResponse;
            try
            {
                seqResponse = message.Type switch
                {
                    MessageType.StartEncode => await _encodingHandler.HandleStartAsync(message, connection, ct),
                    MessageType.CancelEncode => _encodingHandler.HandleCancel(message),
                    MessageType.Shutdown => HandleShutdown(message),
                    _ => IpcMessage.CreateError(message.Id, $"Unknown message type: {message.Type}"),
                };
            }
            catch (Exception ex)
            {
                seqResponse = IpcMessage.CreateError(message.Id, ex.Message, ex.StackTrace);
            }

            if (seqResponse.Type == MessageType.Shutdown)
                break;

            try
            {
                await connection.SendAsync(seqResponse, ct);
            }
            catch (IOException)
            {
                break;
            }
        }
    }

    private IpcMessage HandleMessage(IpcMessage message)
    {
        return message.Type switch
        {
            MessageType.OpenFile => _decodingHandler.HandleOpen(message),
            MessageType.ReadVideo => _decodingHandler.HandleReadVideo(message),
            MessageType.ReadAudio => _decodingHandler.HandleReadAudio(message),
            MessageType.CloseReader => _decodingHandler.HandleClose(message),
            MessageType.UpdateDecoderSettings => _decodingHandler.HandleUpdateDecoderSettings(message),
            MessageType.QueryCodecs => _codecQueryHandler.HandleQueryCodecs(message),
            MessageType.QueryPixelFormats => _codecQueryHandler.HandleQueryPixelFormats(message),
            MessageType.QuerySampleRates => _codecQueryHandler.HandleQuerySampleRates(message),
            MessageType.QueryAudioFormats => _codecQueryHandler.HandleQueryAudioFormats(message),
            MessageType.QueryDefaultCodec => _codecQueryHandler.HandleQueryDefaultCodec(message),
            _ => IpcMessage.CreateError(message.Id, $"Unknown message type: {message.Type}"),
        };
    }

    private static bool IsParallelizable(MessageType type)
    {
        return type is MessageType.ReadVideo
            or MessageType.ReadAudio
            or MessageType.OpenFile
            or MessageType.CloseReader
            or MessageType.UpdateDecoderSettings
            or MessageType.QueryCodecs
            or MessageType.QueryPixelFormats
            or MessageType.QuerySampleRates
            or MessageType.QueryAudioFormats
            or MessageType.QueryDefaultCodec;
    }

    private static IpcMessage HandleShutdown(IpcMessage message)
    {
        return IpcMessage.CreateSimple(message.Id, MessageType.Shutdown);
    }

    public void Dispose()
    {
        _decodingHandler.Dispose();
        _encodingHandler.Dispose();
    }
}
