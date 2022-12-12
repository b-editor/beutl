using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using FFmpeg.AutoGen;

namespace Beutl.Extensions.FFmpeg;

public static class FFmpegLoader
{
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
