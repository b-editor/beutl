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

        public static void Register(string[] args)
        {
            if (OperatingSystem.IsWindows())
            {
                var dir = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.FullName, "ffmpeg");
                Directory.CreateDirectory(dir);
                FFmpegLoader.FFmpegPath = dir;
            }
            FFmpegLoader.LoadFFmpeg();

            PluginBuilder.Configure<Plugin>()
                .With(new EncoderBuilder())
                .With(new DecoderBuilder())
                .Register();
        }
    }
}