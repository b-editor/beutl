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
                break; // パイプ切断
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (message == null)
                break; // 接続終了

            IpcMessage response;
            try
            {
                response = message.Type switch
                {
                    MessageType.OpenFile => _decodingHandler.HandleOpen(message),
                    MessageType.ReadVideo => _decodingHandler.HandleReadVideo(message),
                    MessageType.ReadAudio => _decodingHandler.HandleReadAudio(message),
                    MessageType.CloseReader => _decodingHandler.HandleClose(message),
                    MessageType.UpdateDecoderSettings => _decodingHandler.HandleUpdateDecoderSettings(message),

                    MessageType.StartEncode => await _encodingHandler.HandleStartAsync(message, connection, ct),
                    MessageType.CancelEncode => _encodingHandler.HandleCancel(message),

                    MessageType.QueryCodecs => _codecQueryHandler.HandleQueryCodecs(message),
                    MessageType.QueryPixelFormats => _codecQueryHandler.HandleQueryPixelFormats(message),
                    MessageType.QuerySampleRates => _codecQueryHandler.HandleQuerySampleRates(message),
                    MessageType.QueryAudioFormats => _codecQueryHandler.HandleQueryAudioFormats(message),
                    MessageType.QueryDefaultCodec => _codecQueryHandler.HandleQueryDefaultCodec(message),

                    MessageType.Shutdown => HandleShutdown(message),

                    _ => IpcMessage.CreateError(message.Id, $"Unknown message type: {message.Type}"),
                };
            }
            catch (Exception ex)
            {
                response = IpcMessage.CreateError(message.Id, ex.Message, ex.StackTrace);
            }

            if (response.Type == MessageType.Shutdown)
                break;

            try
            {
                await connection.SendAsync(response, ct);
            }
            catch (IOException)
            {
                break;
            }
        }
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
