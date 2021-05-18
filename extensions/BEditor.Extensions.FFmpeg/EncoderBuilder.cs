using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Extensions.FFmpeg.Encoding;
using BEditor.Media;
using BEditor.Media.Encoding;

namespace BEditor.Extensions.FFmpeg
{
    public class EncoderBuilder : IEncoderBuilder
    {
        public string Name => "FFmpeg";
        public string[] SupportFormats { get; } = new[]
        {
            "Container3GP",
            "Container3GP2",
            "ASF",
            "AVI",
            "FLV",
            "MKV",
            "MOV",
            "MP4",
            "Ogg",
            "WebM",
        };

        public IOutputContainer? Create(string file)
        {
            return new OutputContainer(file);
        }

        public bool IsSupported(string file)
        {
            return Path.GetExtension(file) is ".3gp" or ".3g2" or ".wmv" or ".avi" or
                ".flv" or ".mkv" or ".mov" or ".mp4" or ".ogv" or ".webm";
        }
    }
}