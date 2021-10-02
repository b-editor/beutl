using System;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace BEditor.Extensions.FFmpeg
{
    public sealed class FFmpegInstaller
    {
        public string BasePath { get; set; } = string.Empty;

        public bool IsInstalled()
        {
            if (OperatingSystem.IsWindows())
            {
                return IsInstalledWindows();
            }
            else if (OperatingSystem.IsLinux())
            {
                return IsInstalledLinux();
            }
            else if (OperatingSystem.IsMacOS())
            {
                return IsInstalledMacOS();
            }

            throw new PlatformNotSupportedException();
        }

        public Task<bool> IsInstalledAsync()
        {
            return Task.Run(IsInstalled);
        }

        private static bool IsInstalledMacOS()
        {
            const string? lib = "libavutil.56.dylib";

            if (NativeLibrary.TryLoad(lib, out var ptr))
            {
                NativeLibrary.Free(ptr);

                return true;
            }

            return false;
        }

        private static bool IsInstalledLinux()
        {
            const string? lib = "libavutil.so.56";

            if (NativeLibrary.TryLoad(lib, out var ptr))
            {
                NativeLibrary.Free(ptr);

                return true;
            }

            return false;
        }

        private bool IsInstalledWindows()
        {
            var dlls = new[]
            {
                "avcodec-58.dll",
                "avdevice-58.dll",
                "avfilter-7.dll",
                "avformat-58.dll",
                "avutil-56.dll",
                "postproc-55.dll",
                "swresample-3.dll",
                "swscale-5.dll",
                "ffmpeg.exe",
                "ffprobe.exe",
                "ffplay.exe",
            };

            foreach (var dll in dlls)
            {
                if (!File.Exists(Path.Combine(BasePath, dll)))
                {
                    return false;
                }
            }

            return true;
        }
    }
}