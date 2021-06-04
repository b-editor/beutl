using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using BEditor.Plugin;

using FFMediaToolkit;

namespace BEditor.Extensions.FFmpeg
{
    public class Plugin : PluginObject
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
                    builder.Task(InstallFFmpeg(dir, installer), "Install FFmpeg");
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
                .Register();
        }

        private static Func<IProgressDialog, ValueTask> InstallFFmpeg(string dir, FFmpegInstaller installer)
        {
            return async progress =>
            {
                void downloadprogress(object? s, System.Net.DownloadProgressChangedEventArgs e)
                {
                    progress.Report(e.ProgressPercentage);
                }

                installer.DownloadProgressChanged += downloadprogress;
                await installer.InstallAsync();
                installer.DownloadProgressChanged -= downloadprogress;

                FFmpegLoader.FFmpegPath = dir;

                FFmpegLoader.LoadFFmpeg();
            };
        }
    }
}