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
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return Win32;
                else return FFmpeg;
            }
        }
        public static VideoDecoderFactory FFmpeg { get; } = new FFmpegFactry();
        public static VideoDecoderFactory Win32 { get; } = new Win32Factry();
        // 予定 : public static VideoDecoderFactory VLC { get; }

        public abstract IVideoDecoder Create(string filename);
    }

    internal class FFmpegFactry : VideoDecoderFactory
    {
        public override IVideoDecoder Create(string filename) => new FFmpegDecoder(filename);
    }

    internal class Win32Factry : VideoDecoderFactory
    {
        public override IVideoDecoder Create(string filename) => new Win32Decoder(filename);
    }
}
