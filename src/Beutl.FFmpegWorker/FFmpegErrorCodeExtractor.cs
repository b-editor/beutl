using FFmpegSharp;

namespace Beutl.FFmpegWorker;

/// <summary>
/// Extracts the FFmpeg <c>AVERROR</c> code from a worker-side exception so the IPC error envelope
/// (<see cref="Beutl.FFmpegIpc.Protocol.IpcMessage.ErrorCode"/> /
/// <see cref="Beutl.FFmpegIpc.FFmpegWorkerException.FFmpegErrorCode"/>) can carry it to the host,
/// which maps known codes to user-facing messages without parsing the error text.
/// </summary>
internal static class FFmpegErrorCodeExtractor
{
    public static int? TryGetFFmpegErrorCode(Exception exception)
    {
        for (Exception? ex = exception; ex != null; ex = ex.InnerException)
        {
            if (ex is FFmpegException ffmpegEx)
            {
                // ThrowIfError throws FFmpegException(error), which keeps the AVERROR code; the
                // plain-string constructor does not (ErrorCode == 0).
                if (ffmpegEx.ErrorCode != 0)
                    return ffmpegEx.ErrorCode;
            }
        }

        return null;
    }
}
