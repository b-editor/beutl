using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Transport;
using Beutl.FFmpegWorker.Handlers;

namespace Beutl.FFmpegWorker;

internal sealed class WorkerHost(IpcConnection connection) : IDisposable
{
    private readonly DecodingHandler _decodingHandler = new();
    private readonly EncodingHandler _encodingHandler = new();
    private readonly CodecQueryHandler _codecQueryHandler = new();
    private readonly PreviewEncodingHandler _previewEncoder = new();

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
            if (GetParallelHandler(message.Type) is { } handler)
            {
                StartParallel(message, handler, ct);
                continue;
            }

            // エンコード系・ライフサイクル系は逐次処理
            IpcMessage seqResponse = await HandleSequentialAsync(message, ct);

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

    private void StartParallel(IpcMessage msg, Func<IpcMessage, IpcMessage> handler, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            IpcMessage response;
            try
            {
                response = handler(msg);
            }
            catch (Exception ex)
            {
                response = CreateErrorResponse(msg, ex);
            }

            try
            {
                await connection.SendAsync(response, ct);
            }
            catch (IOException) { }
        }, ct);
    }

    private async Task<IpcMessage> HandleSequentialAsync(IpcMessage message, CancellationToken ct)
    {
        try
        {
            return message.Type switch
            {
                MessageType.StartEncode => await _encodingHandler.HandleStartAsync(message, connection, ct),
                MessageType.CancelEncode => _encodingHandler.HandleCancel(message),
                MessageType.StartPreviewEncoder => _previewEncoder.Start(message),
                MessageType.EncodePreviewFrame => _previewEncoder.Encode(message),
                MessageType.Shutdown => HandleShutdown(message),
                _ => IpcMessage.CreateError(message.Id, $"Unknown message type: {message.Type}"),
            };
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(message, ex);
        }
    }

    private static IpcMessage CreateErrorResponse(IpcMessage request, Exception ex)
    {
        return IpcMessage.CreateError(
            request.Id, ex.Message, ex.StackTrace,
            FFmpegErrorCodeExtractor.TryGetFFmpegErrorCode(ex));
    }

    // The handler of a message that may run in parallel, or null for one handled sequentially.
    private Func<IpcMessage, IpcMessage>? GetParallelHandler(MessageType type)
    {
        return type switch
        {
            MessageType.OpenFile => _decodingHandler.HandleOpen,
            MessageType.ReadVideo => _decodingHandler.HandleReadVideo,
            MessageType.ReadAudio => _decodingHandler.HandleReadAudio,
            MessageType.CloseReader => _decodingHandler.HandleClose,
            MessageType.QueryCodecs => _codecQueryHandler.HandleQueryCodecs,
            MessageType.QueryPixelFormats => _codecQueryHandler.HandleQueryPixelFormats,
            MessageType.QuerySampleRates => _codecQueryHandler.HandleQuerySampleRates,
            MessageType.QueryAudioFormats => _codecQueryHandler.HandleQueryAudioFormats,
            MessageType.QueryEncoderOptions => _codecQueryHandler.HandleQueryEncoderOptions,
            _ => null,
        };
    }

    private static IpcMessage HandleShutdown(IpcMessage message)
    {
        return IpcMessage.CreateSimple(message.Id, MessageType.Shutdown);
    }

    public void Dispose()
    {
        _decodingHandler.Dispose();
        _previewEncoder.Dispose();
    }
}
