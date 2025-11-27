using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

using Avalonia.Threading;

using Beutl.Extensions.FFmpeg.Properties;
using Beutl.Logging;
using Beutl.Services;

using FFmpeg.AutoGen.Abstractions;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.Logging;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg;
#else
namespace Beutl.Extensions.FFmpeg;
#endif

public static class FFmpegLoader
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(FFmpegLoader));
    private static readonly ILogger s_ffmpegLogger = Log.CreateLogger("FFmpeg");
    private static bool s_isInitialized;
    private static bool s_isInitializationFailed;
    private static readonly string s_defaultFFmpegPath;

    static FFmpegLoader()
    {
        s_defaultFFmpegPath = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "ffmpeg");
    }

    public static void Initialize()
    {
        if (s_isInitialized || s_isInitializationFailed)
            return;

        try
        {
            // カスタムリゾルバを設定（ffmpeg.RootPathアクセス前に設定が必要）
            // バージョン付きライブラリが見つからない場合、バージョンなしをフォールバックとして使用
            DynamicallyLoadedBindings.LibrariesPath = GetRootPath();
            DynamicallyLoadedBindings.Initialize();
            FixDependencyIssue();
            SetupLogging();
            var sb = new StringBuilder();
            sb.AppendLine("RequestedVersions:");

            foreach (KeyValuePair<string, int> item in DynamicallyLoadedBindings.LibraryVersionMap)
            {
                sb.AppendLine($"  {item.Key}: {item.Value}");
            }

            sb.AppendLine("Licenses:");
            sb.AppendLine($"  avcodec.{GetVersionString(ffmpeg.avcodec_version())}: {ffmpeg.avcodec_license()}");
            sb.AppendLine($"  avdevice.{GetVersionString(ffmpeg.avdevice_version())}: {ffmpeg.avdevice_license()}");
            sb.AppendLine($"  avfilter.{GetVersionString(ffmpeg.avfilter_version())}: {ffmpeg.avfilter_license()}");
            sb.AppendLine($"  avformat.{GetVersionString(ffmpeg.avformat_version())}: {ffmpeg.avformat_license()}");
            sb.AppendLine($"  avutil.{GetVersionString(ffmpeg.avutil_version())}: {ffmpeg.avutil_license()}");
            sb.AppendLine($"  swresample.{GetVersionString(ffmpeg.swresample_version())}: {ffmpeg.swresample_license()}");
            sb.AppendLine($"  swscale.{GetVersionString(ffmpeg.swscale_version())}: {ffmpeg.swscale_license()}");
            s_logger.LogInformation("{VersionAndLicense}", sb.ToString());

            s_isInitialized = true;
        }
        catch
        {
            s_isInitializationFailed = true;
            NotificationService.ShowError(
                Strings.FFmpegError,
                Strings.Make_sure_you_have_FFmpeg_installed,
                onActionButtonClick: ShowInstallDialog,
                actionButtonText: Strings.Install);
        }

        static string GetVersionString(uint version)
        {
            uint major = version >> 16 & 0xFF;
            uint minor = version >> 8 & 0xFF;
            uint patch = version & 0xFF;
            return $"{major}.{minor}.{patch}";
        }
    }

    public static void SetupLogging()
    {
        FFmpegSharp.FFmpegLog.SetupLogging(
            logWrite: (s, i) =>
            {
                var level = (FFmpegSharp.LogLevel)i;
                var convertedLevel = level switch
                {
                    FFmpegSharp.LogLevel.Debug => LogLevel.Debug,
                    FFmpegSharp.LogLevel.Warning => LogLevel.Warning,
                    FFmpegSharp.LogLevel.Error => LogLevel.Error,
                    FFmpegSharp.LogLevel.Fatal => LogLevel.Critical,
                    _ => LogLevel.Information
                };
                s_ffmpegLogger.Log(convertedLevel, "{OriginalLevel} {FFmpegLog}", level, s.TrimEnd('\n').TrimEnd('\r'));
            });
    }

    private static void ShowInstallDialog()
    {
        Dispatcher.UIThread.Post(() =>
        {
            FFmpegInstallDialogViewModel viewModel = new();
            FFmpegInstallDialog dialog = new()
            {
                DataContext = viewModel
            };

            // Subscribe to completion to close dialog
            viewModel.IsCompleted.Subscribe(completed =>
            {
                if (completed)
                {
                    Dispatcher.UIThread.Post(() => dialog.Hide(ContentDialogResult.Primary));
                }
            });

            dialog.ShowAsync();
            viewModel.Start();
        });
    }

    private static void FixDependencyIssue()
    {
        // FFmpeg 8.0 以降では postproc は廃止されているため、依存関係を修正する
        // public static readonly Dictionary<string, string[]> LibraryDependenciesMap =
        //     new()
        //     {
        //         { "avcodec", new[] { "avutil", "swresample" } },
        //         { "avdevice", new[] { "avcodec", "avfilter", "avformat", "avutil" } },
        //         { "avfilter", new[] { "avcodec", "avformat", "avutil", "postproc", "swresample", "swscale" } },
        //         { "avformat", new[] { "avcodec", "avutil" } },
        //         { "avutil", new string[0] },
        //         { "postproc", new[] { "avutil" } },
        //         { "swresample", new[] { "avutil" } },
        //         { "swscale", new[] { "avutil" } }
        //     };

        FunctionResolverBase.LibraryDependenciesMap["avfilter"] = ["avcodec", "avformat", "avutil", "swresample", "swscale"];
        FunctionResolverBase.LibraryDependenciesMap.Remove("postproc");
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
            var libraryPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH")?.Split(Path.PathSeparator) ?? [];
            paths.AddRange(libraryPath);
        }
        else if (OperatingSystem.IsMacOS())
        {
            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.X64:
                    paths.Add("/usr/local/opt/ffmpeg@8/lib");
                    paths.Add("/usr/local/opt/ffmpeg/lib");
                    paths.Add("/usr/local/lib");
                    break;
                case Architecture.Arm64:
                    paths.Add("/opt/homebrew/opt/ffmpeg@8/lib");
                    paths.Add("/opt/homebrew/opt/ffmpeg/lib");
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
        foreach (KeyValuePair<string, int> item in DynamicallyLoadedBindings.LibraryVersionMap)
        {
            string versionedLibraryName = string.Empty;
            if (OperatingSystem.IsWindows())
            {
                versionedLibraryName = $"{item.Key}-{item.Value}.dll";
            }
            else if (OperatingSystem.IsLinux())
            {
                versionedLibraryName = $"lib{item.Key}.so.{item.Value}";
            }
            else if (OperatingSystem.IsMacOS())
            {
                versionedLibraryName = $"lib{item.Key}.{item.Value}.dylib";
            }

            if (!files.Any(x => x.Contains(versionedLibraryName)))
            {
                return false;
            }
        }

        return true;
    }
}
