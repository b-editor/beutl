namespace Beutl.FFmpegIpc;

public sealed class FFmpegWorkerException : Exception
{
    public FFmpegWorkerException(string message, string? remoteStackTrace = null)
        : base(message)
    {
        RemoteStackTrace = remoteStackTrace;
    }

    public string? RemoteStackTrace { get; }

    public override string ToString()
    {
        if (RemoteStackTrace != null)
            return $"{base.ToString()}\n--- Remote stack trace ---\n{RemoteStackTrace}";
        return base.ToString();
    }
}
