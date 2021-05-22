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

        public event EventHandler? StartInstall;
        public event EventHandler? Installed;
        public event EventHandler<AsyncCompletedEventArgs>? DownloadCompleted;
        public event EventHandler<DownloadProgressChangedEventArgs>? DownloadProgressChanged;

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

        public async Task InstallAsync()
        {
            const string url = "https://beditor.net/repo/ffmpeg.zip";
            StartInstall?.Invoke(this, EventArgs.Empty);

            using var client = new WebClient();

            var tmp = Path.GetTempFileName();
            client.DownloadFileCompleted += Client_DownloadFileCompleted;
            client.DownloadProgressChanged += Client_DownloadProgressChanged;

            await client.DownloadFileTaskAsync(url, tmp);

            await using (var stream = new FileStream(tmp, FileMode.Open))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var destdir = BasePath;

                if (!Directory.Exists(destdir))
                {
                    Directory.CreateDirectory(destdir);
                }

                foreach (var entry in zip.Entries)
                {
                    var file = Path.GetFileName(entry.FullName);
                    await using var deststream = new FileStream(Path.Combine(destdir, file), FileMode.Create);
                    await using var srcstream = entry.Open();

                    await srcstream.CopyToAsync(deststream);
                }
            }

            File.Delete(tmp);

            Installed?.Invoke(this, EventArgs.Empty);
            client.DownloadFileCompleted -= Client_DownloadFileCompleted;
            client.DownloadProgressChanged -= Client_DownloadProgressChanged;
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

        private void Client_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            DownloadProgressChanged?.Invoke(sender, e);
        }

        private void Client_DownloadFileCompleted(object? sender, AsyncCompletedEventArgs e)
        {
            DownloadCompleted?.Invoke(sender, e);
        }
    }
}