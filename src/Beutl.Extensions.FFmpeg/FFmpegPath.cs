using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

using Beutl.Extensions.FFmpeg.Properties;
using Beutl.Services;

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
    private static bool s_isInitialized;
    private static readonly string s_defaultFFmpegExePath;
    private static readonly string s_defaultFFmpegPath;

    static FFmpegLoader()
    {
        s_defaultFFmpegPath = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "ffmpeg");
        s_defaultFFmpegExePath = Path.Combine(s_defaultFFmpegPath, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
    }

    public static void Initialize()
    {
        if (s_isInitialized)
            return;

        try
        {
            ffmpeg.RootPath = GetRootPath();
            var sb = new StringBuilder();
            sb.AppendLine("Versions:");

            foreach (KeyValuePair<string, int> item in ffmpeg.LibraryVersionMap)
            {
                sb.AppendLine($"  {item.Key}: {item.Value}");
            }

            sb.AppendLine("Licenses:");
            sb.AppendLine($"  avcodec: {ffmpeg.avcodec_license()}");
            sb.AppendLine($"  avdevice: {ffmpeg.avdevice_license()}");
            sb.AppendLine($"  avfilter: {ffmpeg.avfilter_license()}");
            sb.AppendLine($"  avformat: {ffmpeg.avformat_license()}");
            sb.AppendLine($"  avutil: {ffmpeg.avutil_license()}");
            sb.AppendLine($"  postproc: {ffmpeg.postproc_license()}");
            sb.AppendLine($"  swresample: {ffmpeg.swresample_license()}");
            sb.AppendLine($"  swscale: {ffmpeg.swscale_license()}");
            s_logger.LogInformation("{VersionAndLicense}", sb.ToString());

            s_isInitialized = true;
        }
        catch
        {
            NotificationService.ShowError(
                Strings.FFmpegError,
                Strings.Make_sure_you_have_FFmpeg_installed,
                onActionButtonClick: OpenDocumentUrl,
                actionButtonText: Beutl.Language.Strings.OpenDocument);

            throw;
        }
    }

    private static void OpenDocumentUrl()
    {
        Process.Start(new ProcessStartInfo("https://github.com/b-editor/beutl-docs/blob/main/ja/ffmpeg-install.md")
        {
            UseShellExecute = true,
            Verb = "open"
        });
    }

    public static string GetExecutable()
    {
        var paths = new List<string>
        {
            s_defaultFFmpegExePath,
            Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg")
        };

        if (OperatingSystem.IsLinux())
        {
            paths.Add("/usr/bin/ffmpeg");
        }
        if (OperatingSystem.IsMacOS())
        {
            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.X64:
                    paths.Add("/usr/local/lib/ffmpeg@6/bin/ffmpeg");
                    paths.Add("/usr/local/bin/ffmpeg");
                    break;
                case Architecture.Arm64:
                    paths.Add("/opt/homebrew/opt/ffmpeg@6/bin/ffmpeg");
                    paths.Add("/opt/homebrew/bin/ffmpeg");
                    break;
            }
        }

        foreach (string item in paths)
        {
            if (File.Exists(item))
            {
                return item;
            }
        }

        throw new InvalidOperationException();
    }

    public static string GetRootPath()
    {
        var paths = new List<string>
        {
            s_defaultFFmpegPath,
            Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.FullName,
            AppContext.BaseDirectory
        };

        if (OperatingSystem.IsWindows())
        {
            paths.Add(Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.FullName,
                "runtimes",
                Environment.Is64BitProcess ? "win-x64" : "win-x86",
                "native"));

            paths.Add(Path.Combine(AppContext.BaseDirectory,
                "runtimes",
                Environment.Is64BitProcess ? "win-x64" : "win-x86",
                "native"));
        }
        else if (OperatingSystem.IsLinux())
        {
            paths.Add($"/usr/lib/{(Environment.Is64BitProcess ? "x86_64" : "x86")}-linux-gnu");
        }
        else if (OperatingSystem.IsMacOS())
        {
            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.X64:
                    paths.Add("/usr/local/opt/ffmpeg@6/lib");
                    paths.Add("/usr/local/lib");
                    break;
                case Architecture.Arm64:
                    paths.Add("/opt/homebrew/opt/ffmpeg@6/lib");
                    paths.Add("/opt/homebrew/lib");
                    break;
            }
        }

        foreach (string item in paths)
        {
            if (LibrariesExists(item))
            {
                return item;
            }
        }

        throw new InvalidOperationException();
    }

    private static bool LibrariesExists(string basePath)
    {
        if (!Directory.Exists(basePath)) return false;

        string[] files = Directory.GetFiles(basePath);
        foreach (KeyValuePair<string, int> item in ffmpeg.LibraryVersionMap)
        {
            ///usr/local/lib/libavformat.60.16.100.dylib
            if (!files.Any(x => x.Contains(item.Key)))
            {
                return false;
            }
        }

        return true;
    }
}
