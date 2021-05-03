using System;
using System.IO;
using System.Runtime.InteropServices;

using BEditor.Media.Interop;

using FFmpeg.AutoGen;

namespace BEditor.Media
{
    /// <summary>
    /// Contains methods for managing FFmpeg libraries.
    /// </summary>
    public static class FFmpegLoader
    {
        private static LogLevel _logLevel = LogLevel.Error;
        private static bool _isPathSet;

        /// <summary>
        /// Delegate for log message callback.
        /// </summary>
        /// <param name="message">The message.</param>
        public delegate void LogCallbackDelegate(string message);

        /// <summary>
        /// Log message callback event.
        /// </summary>
        public static event LogCallbackDelegate? LogCallback;

        /// <summary>
        /// Gets or sets the verbosity level of FFMpeg logs printed to standard error/output.
        /// Default value is <see cref="LogLevel.Error"/>.
        /// </summary>
        public static LogLevel LogVerbosity
        {
            get => _logLevel;
            set
            {
                if (IsFFmpegLoaded)
                {
                    ffmpeg.av_log_set_level((int)value);
                }

                _logLevel = value;
            }
        }

        /// <summary>
        /// Gets or sets path to the directory containing FFmpeg binaries.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when FFmpeg was already loaded.</exception>
        /// <exception cref="DirectoryNotFoundException">Thrown when specified directory does not exist.</exception>
        public static string FFmpegPath
        {
            get => ffmpeg.RootPath ?? string.Empty;
            set
            {
                if (IsFFmpegLoaded)
                {
                    throw new InvalidOperationException("FFmpeg libraries were already loaded!");
                }

                if (!Directory.Exists(value))
                {
                    throw new DirectoryNotFoundException("The specified FFmpeg directory does not exist!");
                }

                ffmpeg.RootPath = value;
                _isPathSet = true;
            }
        }

        /// <summary>
        /// Gets the FFmpeg version info string.
        /// Empty when FFmpeg libraries were not yet loaded.
        /// </summary>
        public static string FFmpegVersion { get; private set; } = string.Empty;

        /// <summary>
        /// Gets a value indicating whether the loaded FFmpeg binary files are licensed under the GPL.
        /// Null when FFmpeg libraries were not yet loaded.
        /// </summary>
        public static bool? IsFFmpegGplLicensed { get; private set; }

        /// <summary>
        /// Gets the FFmpeg license text
        /// Empty when FFmpeg libraries were not yet loaded.
        /// </summary>
        public static string FFmpegLicense { get; private set; } = string.Empty;

        /// <summary>
        /// Gets a value indicating whether the FFmpeg binary files were successfully loaded.
        /// </summary>
        internal static bool IsFFmpegLoaded { get; private set; }

        /// <summary>
        /// Manually loads FFmpeg libraries from the specified <see cref="FFmpegPath"/> (or the default path for current platform if not set).
        /// If you will not call this method, FFmpeg will be loaded while opening or creating a video file.
        /// </summary>
        /// <exception cref="DirectoryNotFoundException">
        /// Thrown when default FFmpeg directory does not exist.
        /// On Windows you have to specify a path to a directory containing the FFmpeg shared build DLL files.
        /// </exception>
        /// <exception cref="DllNotFoundException">
        /// Thrown when required FFmpeg libraries do not exist or when you try to load 64bit binaries from 32bit application process.
        /// </exception>
        public static void LoadFFmpeg()
        {
            if (IsFFmpegLoaded)
            {
                return;
            }

            if (!_isPathSet)
            {
                try
                {
                    FFmpegPath = NativeMethods.GetFFmpegDirectory();
                }
                catch (DirectoryNotFoundException)
                {
                    throw new DirectoryNotFoundException(@"Cannot found the default FFmpeg directory.
On Windows you have to set ""FFmpegLoader.FFmpegPath"" with full path to the directory containing FFmpeg shared build "".dll"" files
For more informations please see https://github.com/radek-k/BEditor.Media#setup");
                }
            }

            try
            {
                FFmpegVersion = ffmpeg.av_version_info();
                FFmpegLicense = ffmpeg.avcodec_license();
                IsFFmpegGplLicensed = FFmpegLicense.StartsWith("GPL");
            }
            catch (DllNotFoundException ex)
            {
                HandleLibraryLoadError(ex);
            }

            IsFFmpegLoaded = true;
            LogVerbosity = _logLevel;
        }

        /// <summary>
        /// Start logging ffmpeg output.
        /// </summary>
        public static unsafe void SetupLogging()
        {
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);

            // do not convert to local function
            av_log_set_callback_callback logCallback = (p0, level, format, vl) =>
            {
                if (level > ffmpeg.av_log_get_level()) return;

                const int lineSize = 1024;
                var lineBuffer = stackalloc byte[lineSize];
                var printPrefix = 1;

                ffmpeg.av_log_format_line(p0, level, format, vl, lineBuffer, lineSize, &printPrefix);
                var line = Marshal.PtrToStringAnsi((IntPtr)lineBuffer)!;

                LogCallback?.Invoke(line);
            };

            ffmpeg.av_log_set_callback(logCallback);
        }

        /// <summary>
        /// Throws a FFmpeg library loading exception.
        /// </summary>
        /// <param name="exception">The original exception.</param>
        internal static void HandleLibraryLoadError(Exception exception)
        {
            throw new DllNotFoundException($"Cannot load required FFmpeg libraries from {FFmpegPath} directory.\nMake sure the \"Build\"Prefer 32-bit\" option in the project settings is turned off.\nFor more informations please see https://github.com/radek-k/BEditor.Media#setup", exception);
        }
    }
}