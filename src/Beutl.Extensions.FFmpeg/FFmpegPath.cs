using System.Reflection;

using FFmpeg.AutoGen;

using Microsoft.Extensions.Logging;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg;
#else
namespace Beutl.Extensions.FFmpeg;
#endif

public static class FFmpegLoader
{
    private static readonly ILogger s_logger = BeutlApplication.Current.LoggerFactory.CreateLogger(typeof(FFmpegLoader));
    private static bool _isInitialized;

    static FFmpegLoader()
    {
        ffmpeg.RootPath = GetRootPath();
        _isInitialized = true;
    }

    public static void Initialize()
    {
        if (!_isInitialized)
            throw new InvalidOperationException("Not initialized.");

        s_logger.LogInformation("avcodec_license() {License}", ffmpeg.avcodec_license());
        s_logger.LogInformation("avdevice_license() {License}", ffmpeg.avdevice_license());
        s_logger.LogInformation("avfilter_license() {License}", ffmpeg.avfilter_license());
        s_logger.LogInformation("avformat_license() {License}", ffmpeg.avformat_license());
        s_logger.LogInformation("avutil_license() {License}", ffmpeg.avutil_license());
        s_logger.LogInformation("postproc_license() {License}", ffmpeg.postproc_license());
        s_logger.LogInformation("swresample_license() {License}", ffmpeg.swresample_license());
        s_logger.LogInformation("swscale_license() {License}", ffmpeg.swscale_license());
    }

    public static string GetExecutable()
    {
        if (Environment.GetEnvironmentVariable("FFMPEG_PATH") is string exePath
            && File.Exists(exePath))
        {
            return exePath;
        }

        if (OperatingSystem.IsWindows())
        {
            exePath = Path.Combine(
                Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.FullName,
                "runtimes",
                Environment.Is64BitProcess ? "win-x64" : "win-x86",
                "native",
                "ffmpeg.exe");
        }
        else if (OperatingSystem.IsLinux())
        {
            exePath = "/usr/bin/ffmpeg";
        }
        else
        {
            exePath = null!;
        }

        if (!File.Exists(exePath))
        {
            throw new InvalidOperationException();
        }
        else
        {
            return exePath;
        }
    }

    public static string GetRootPath()
    {
        if (Environment.GetEnvironmentVariable("FFMPEG_ROOT_PATH") is string rootPath
            && Directory.Exists(rootPath))
        {
            return rootPath;
        }

        if (OperatingSystem.IsWindows())
        {
            rootPath = Path.Combine(
                Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.FullName,
                "runtimes",
                Environment.Is64BitProcess ? "win-x64" : "win-x86",
                "native");
        }
        else if (OperatingSystem.IsLinux())
        {
            rootPath = $"/usr/lib/{(Environment.Is64BitProcess ? "x86_64" : "x86")}-linux-gnu";
        }
        else
        {
            rootPath = null!;
        }

        if (!Directory.Exists(rootPath))
        {
            throw new InvalidOperationException();
        }
        else
        {
            return rootPath;
        }
    }
}
