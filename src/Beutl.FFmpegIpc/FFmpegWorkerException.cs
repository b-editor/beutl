namespace Beutl.FFmpegIpc;

public sealed class FFmpegWorkerException : Exception
{
    // The original two-argument constructor is preserved so extensions compiled against an older
    // Beutl.FFmpegIpc keep binding (an optional parameter would change the CLR signature to
    // .ctor(string, string, int?) and fail with MissingMethodException at runtime).
    public FFmpegWorkerException(string message, string? remoteStackTrace = null)
        : this(message, remoteStackTrace, ffmpegErrorCode: null)
    {
    }

    public FFmpegWorkerException(string message, string? remoteStackTrace = null, int? ffmpegErrorCode = null)
        : base(message)
    {
        RemoteStackTrace = remoteStackTrace;
        FFmpegErrorCode = ffmpegErrorCode;
    }

    public string? RemoteStackTrace { get; }

    /// <summary>
    /// The FFmpeg <c>AVERROR</c> code reported by the worker (negative, e.g. -1094995529 for
    /// <c>AVERROR_INVALIDDATA</c>). Null when the worker-side failure was not an FFmpeg error.
    /// </summary>
    public int? FFmpegErrorCode { get; }

    public override string ToString()
    {
        if (RemoteStackTrace != null)
            return $"{base.ToString()}\n--- Remote stack trace ---\n{RemoteStackTrace}";
        return base.ToString();
    }
}
