namespace Beutl.FFmpegIpc;

public sealed class FFmpegWorkerException : Exception
{
    public FFmpegWorkerException(string message, string? remoteStackTrace = null, int? ffmpegErrorCode = null)
        : base(message)
    {
        RemoteStackTrace = remoteStackTrace;
        FFmpegErrorCode = ffmpegErrorCode;
    }

    public string? RemoteStackTrace { get; }

    /// <summary>FFmpeg AVERROR code, if available.</summary>
    public int? FFmpegErrorCode { get; }

    public override string ToString()
    {
        if (RemoteStackTrace != null)
            return $"{base.ToString()}\n--- Remote stack trace ---\n{RemoteStackTrace}";
        return base.ToString();
    }
}
