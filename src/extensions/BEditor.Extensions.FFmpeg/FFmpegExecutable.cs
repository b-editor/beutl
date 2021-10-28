using System;
using System.IO;
using System.Reflection;

namespace BEditor.Extensions.FFmpeg
{
    internal static class FFmpegExecutable
    {
        public static string GetExecutable()
        {
            if (OperatingSystem.IsWindows()) return Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.FullName, "ffmpeg", "ffmpeg.exe");
            else if (OperatingSystem.IsLinux()) return "/usr/bin/ffmpeg";
            else if (OperatingSystem.IsMacOS()) return "/usr/local/opt/ffmpeg";
            else throw new PlatformNotSupportedException();
        }

        public static string GetFFprobe()
        {
            if (OperatingSystem.IsWindows()) return Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.FullName, "ffmpeg", "ffprobe.exe");
            else if (OperatingSystem.IsLinux()) return "/usr/bin/ffprobe";
            else if (OperatingSystem.IsMacOS()) return "/usr/local/opt/ffprobe";
            else throw new PlatformNotSupportedException();
        }
    }
}