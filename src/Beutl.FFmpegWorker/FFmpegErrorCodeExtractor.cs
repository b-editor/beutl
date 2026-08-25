using FFmpegSharp;

namespace Beutl.FFmpegWorker;

/// <summary>Gets an FFmpeg error code from an exception chain.</summary>
internal static class FFmpegErrorCodeExtractor
{
    public static int? TryGetFFmpegErrorCode(Exception exception)
    {
        for (Exception? ex = exception; ex != null; ex = ex.InnerException)
        {
            if (ex is FFmpegException ffmpegEx)
            {
                // String-created exceptions have no error code.
                if (ffmpegEx.ErrorCode != 0)
                    return ffmpegEx.ErrorCode;
            }
        }

        return null;
    }
}
