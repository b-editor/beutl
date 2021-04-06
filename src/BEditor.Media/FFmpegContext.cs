using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using FFMediaToolkit;

namespace BEditor.Media
{
    internal class FFmpegContext
    {
        [ModuleInitializer]
        public static void LoadFFmpeg()
        {
            if (OperatingSystem.IsWindows())
            {
                FFmpegLoader.FFmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
            }

            FFmpegLoader.LoadFFmpeg();
        }
    }
}
