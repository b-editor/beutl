using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using BEditor.Plugin;

using FFMediaToolkit;

namespace BEditor.Extensions.FFmpeg
{
    public sealed class Plugin : PluginObject
    {
        public Plugin(PluginConfig config) : base(config)
        {
        }

        public override string PluginName => "BEditor.Extensions.FFmpeg";

        public override string Description => "FFmpeg decoding and encoding";

        public override SettingRecord Settings { get; set; } = new();

        public override Guid Id { get; } = Guid.Parse("6C7EA15C-B6C2-46A3-AD11-22EB5253C346");

        public static void Register()
        {
            var builder = PluginBuilder.Configure<Plugin>();

            var dir = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.FullName, "ffmpeg");
            var installer = new FFmpegInstaller
            {
                BasePath = dir
            };

            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(dir);
                if (!installer.IsInstalled())
                {
                    builder.Task(InstallFFmpeg(dir), "Install FFmpeg");
                }
                else
                {
                    FFmpegLoader.FFmpegPath = dir;
                    FFmpegLoader.LoadFFmpeg();
                }
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                if (!installer.IsInstalled())
                {
                    return;
                }
                else
                {
                    FFmpegLoader.LoadFFmpeg();
                }
            }

            builder.With(new RegisterdEncoding())
                .With(new RegisterdDecoding())
                .FileMenu(new OpenMediaInfo())
                .Register();
        }

        private static async ValueTask DownloadFFmpeg(IProgressDialog progress, string file)
        {
            const string url = "https://beditor.net/repo/ffmpeg.zip";

            using var client = new HttpClient();
            using var fs = new FileStream(file, FileMode.Create);
            client.DefaultRequestHeaders.ExpectContinue = false;
            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (response.Content.Headers.ContentLength != null)
            {
                progress.Maximum = (long)response.Content.Headers.ContentLength;
            }

            await SaveAsync(progress, response, fs, 0x10000);
        }

        private static async ValueTask SaveAsync(IProgressDialog progress, HttpResponseMessage response, FileStream fs, int ticks)
        {
            using var stream = await response.Content.ReadAsStreamAsync();
            while (true)
            {
                var buffer = new byte[ticks];
                var t = await stream.ReadAsync(buffer.AsMemory(0, ticks));
                // 0バイト読みこんだら終わり
                if (t == 0) break;

                progress.Value += t;

                await fs.WriteAsync(buffer.AsMemory(0, t));
            }
        }

        private static Func<IProgressDialog, ValueTask> InstallFFmpeg(string dir)
        {
            return async progress =>
            {
                var tmp = Path.GetTempFileName();
                await DownloadFFmpeg(progress, tmp);

                using (var stream = new FileStream(tmp, FileMode.Open))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    progress.IsIndeterminate = true;

                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    foreach (var entry in zip.Entries)
                    {
                        var file = Path.GetFileName(entry.FullName);
                        await using var deststream = new FileStream(Path.Combine(dir, file), FileMode.Create);
                        await using var srcstream = entry.Open();

                        await srcstream.CopyToAsync(deststream);
                    }
                    progress.IsIndeterminate = false;
                }

                File.Delete(tmp);

                FFmpegLoader.FFmpegPath = dir;
                FFmpegLoader.LoadFFmpeg();
            };
        }

        private sealed class OpenMediaInfo : FileMenu
        {
            public OpenMediaInfo()
            {
                Name = "Open media info";
                SupportedExtensions = new string[]
                {
                    "*.mp3",
                    "*.ogg",
                    "*.wav",
                    "*.aac",
                    "*.wma",
                    "*.m4a",
                    "*.opus",

                    "*.avi",
                    "*.mov",
                    "*.wmv",
                    "*.mp4",
                    "*.webm",
                    "*.mkv",
                    "*.flv",
                    "*.264",
                    "*.mpeg",
                    "*.ts",
                    "*.mts",
                    "*.m2ts",
                };
            }

            protected override void OnExecute(string arg)
            {
                base.OnExecute(arg);

            }
        }
    }
}