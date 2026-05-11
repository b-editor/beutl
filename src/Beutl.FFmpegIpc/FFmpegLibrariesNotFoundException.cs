namespace Beutl.FFmpegIpc;

public sealed class FFmpegLibrariesNotFoundException : Exception
{
    public FFmpegLibrariesNotFoundException(string message) : base(message)
    {
    }
}
