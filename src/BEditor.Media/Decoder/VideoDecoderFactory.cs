using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Media.Decoder
{
    public abstract class VideoDecoderFactory
    {
        public static VideoDecoderFactory Default
        {
            get => FFmpeg;
        }
        public static VideoDecoderFactory FFmpeg { get; } = new FFmpegFactry();

        public abstract IMediaDecoder Create(string filename);
    }

    internal class FFmpegFactry : VideoDecoderFactory
    {
        public override IMediaDecoder Create(string filename) => new FFmpegDecoder(filename);
    }
}
