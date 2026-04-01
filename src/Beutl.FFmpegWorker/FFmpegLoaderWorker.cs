using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using FFmpeg.AutoGen.Abstractions;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;

namespace Beutl.FFmpegWorker;

internal static class FFmpegLoaderWorker
{
    private static readonly string s_defaultFFmpegPath;

    static FFmpegLoaderWorker()
    {
        s_defaultFFmpegPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".beutl", "ffmpeg");
    }

    public static void Initialize()
    {
        DynamicallyLoadedBindings.LibrariesPath = GetRootPath();
        DynamicallyLoadedBindings.Initialize();
        FixDependencyIssue();
        SetupLogging();

        var sb = new StringBuilder();
        sb.AppendLine("FFmpegWorker loaded versions:");
        sb.AppendLine($"  avcodec: {GetVersionString(ffmpeg.avcodec_version())}");
        sb.AppendLine($"  avformat: {GetVersionString(ffmpeg.avformat_version())}");
        sb.AppendLine($"  avutil: {GetVersionString(ffmpeg.avutil_version())}");
        sb.AppendLine($"  swresample: {GetVersionString(ffmpeg.swresample_version())}");
        sb.AppendLine($"  swscale: {GetVersionString(ffmpeg.swscale_version())}");
        Console.Error.Write(sb.ToString());
    }

    private static void FixDependencyIssue()
    {
        FunctionResolverBase.LibraryDependenciesMap["avfilter"] = ["avcodec", "avformat", "avutil", "swresample", "swscale"];
        FunctionResolverBase.LibraryDependenciesMap.Remove("postproc");
    }

    private static void SetupLogging()
    {
        FFmpegSharp.FFmpegLog.SetupLogging(
            logWrite: (s, i) =>
            {
                var level = (FFmpegSharp.LogLevel)i;
                if (level >= FFmpegSharp.LogLevel.Warning)
                {
                    Console.Error.Write($"[ffmpeg:{level}] {s}");
                }
            });
    }

    private static string GetRootPath()
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
                "runtimes", Environment.Is64BitProcess ? "win-x64" : "win-x86", "native"));
            paths.Add(Path.Combine(AppContext.BaseDirectory,
                "runtimes", Environment.Is64BitProcess ? "win-x64" : "win-x86", "native"));
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

        foreach (string path in paths)
        {
            if (LibrariesExists(path))
                return path;
        }

        throw new InvalidOperationException("FFmpeg libraries not found");
    }

    private static bool LibrariesExists(string basePath)
    {
        if (!Directory.Exists(basePath)) return false;

        string[] files = Directory.GetFiles(basePath);
        foreach (KeyValuePair<string, int> item in DynamicallyLoadedBindings.LibraryVersionMap)
        {
            string versionedLibraryName =
                OperatingSystem.IsWindows() ? $"{item.Key}-{item.Value}.dll" :
                OperatingSystem.IsLinux() ? $"lib{item.Key}.so.{item.Value}" :
                OperatingSystem.IsMacOS() ? $"lib{item.Key}.{item.Value}.dylib" :
                throw new InvalidOperationException();

            if (!files.Any(x => x.Contains(versionedLibraryName)))
                return false;
        }

        return true;
    }

    private static string GetVersionString(uint version)
    {
        uint major = version >> 16 & 0xFF;
        uint minor = version >> 8 & 0xFF;
        uint patch = version & 0xFF;
        return $"{major}.{minor}.{patch}";
    }
}
