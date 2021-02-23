using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using FFMediaToolkit;

namespace BEditor.Media
{
    internal class FFmpegContext
    {
        public static readonly FFmpegContext Current = new();

        public FFmpegContext()
        {
            if (OperatingSystem.IsWindows())
            {
                FFmpegLoader.FFmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
            }

            FFmpegLoader.LoadFFmpeg();
        }
    }
}
