using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.Transport;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Extensions.FFmpeg.Decoding;

// Requests that readers send to the decode worker, often from the render thread. A worker that exited fails them
// at once, because its connection faults, but one that stopped answering would block the waiting thread for good,
// so every wait has a time limit.
internal static class FFmpegWorkerRequests
{
    private static readonly ILogger s_logger = Log.CreateLogger<FFmpegReaderProxy>();

    // Generous, so that a slow open or decode, such as a seek through a long GOP or a disk spinning up, is not
    // taken for a worker that stopped answering.
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    // Throws TimeoutException when the worker does not answer within the time limit. The request is not cancelled,
    // so an answer that arrives later still reaches onLateAnswer: a reader the worker opened after all must be
    // closed, or its decoder stays in the worker.
    public static TResponse Send<TRequest, TResponse>(
        IpcConnection connection, MessageType requestType, MessageType responseType, TRequest payload,
        TimeSpan timeout, Action<TResponse>? onLateAnswer = null)
    {
        Task<TResponse> request = connection.RequestAsync<TRequest, TResponse>(requestType, responseType, payload)
            .AsTask();
        try
        {
            return request.WaitAsync(timeout).GetAwaiter().GetResult();
        }
        catch (TimeoutException)
        {
            _ = request.ContinueWith(
                t =>
                {
                    if (t.IsFaulted)
                        _ = t.Exception;
                    else if (t.IsCompletedSuccessfully)
                        onLateAnswer?.Invoke(t.Result);
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);

            throw new TimeoutException(
                $"The FFmpeg worker did not answer {requestType} within {timeout.TotalSeconds:0.###} seconds.");
        }
    }

    // The returned task never faults; it completes when the worker answers, which it does only after disposing the
    // reader, or at the time limit. Like Send, the limit bounds only the wait: a close still queued behind a write to
    // a worker that stopped reading its pipe is delivered once it reads again, or its decoder would stay there.
    public static Task CloseReader(IpcConnection connection, int readerId, TimeSpan timeout)
    {
        // fire-and-forget: UIスレッドからの呼び出しでデッドロックしないよう
        // 同期ブロックを避けて非同期で送信
        return Task.Run(async () =>
        {
            Task<IpcMessage>? request = null;
            try
            {
                request = connection.SendAndReceiveAsync(
                        IpcMessage.Create(connection.NextId(), MessageType.CloseReader,
                            new CloseReaderRequest { ReaderId = readerId }))
                    .AsTask();
                await request.WaitAsync(timeout);
            }
            catch (TimeoutException) when (request is not null)
            {
                s_logger.LogWarning(
                    "The FFmpeg worker has not closed reader {ReaderId} within {Seconds} seconds; the close stays queued",
                    readerId, timeout.TotalSeconds);
                _ = request.ContinueWith(
                    t => s_logger.LogWarning(t.Exception, "Failed to close FFmpeg reader {ReaderId} on worker", readerId),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(ex, "Failed to close FFmpeg reader {ReaderId} on worker", readerId);
            }
        });
    }
}
