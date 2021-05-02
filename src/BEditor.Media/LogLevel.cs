using FFmpeg.AutoGen;

namespace BEditor.Media
{
    /// <summary>
    /// FFMpeg logging verbosity levels.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// Doesn't print any messages.
        /// </summary>
        Quiet = ffmpeg.AV_LOG_QUIET,

        /// <summary>
        /// Prints only error messages.
        /// </summary>
        Error = ffmpeg.AV_LOG_ERROR,

        /// <summary>
        /// Prints error and warning messages.
        /// </summary>
        Warning = ffmpeg.AV_LOG_WARNING,

        /// <summary>
        /// Prints errors, warnings and informational messages.
        /// </summary>
        Info = ffmpeg.AV_LOG_INFO,

        /// <summary>
        /// Prints the most detailed messages.
        /// </summary>
        Verbose = ffmpeg.AV_LOG_VERBOSE,
    }
}
