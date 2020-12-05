using System.Diagnostics;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Media.Encoder
{
    public class FFmpegVideoEncoder : VideoEncoder
    {
        public FFmpegVideoEncoder(string fileName, int fps, int width, int height, int bitrate) : base(fileName, fps, width, height)
        {

        }

        public override void Write(Image<BGRA32> image)
        {

        }

        public override void Dispose()
        {

        }


        public int Bitrate { get; }
    }
}
