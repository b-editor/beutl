using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Media;
using BEditor.Drawing;

namespace BEditor.Core.Media.Decoder
{
    public class FFmpegVideoDecoder : VideoDecoder
    {
        public FFmpegVideoDecoder(string fileName) : base(fileName)
        {

        }

        public override int Fps { get; }
        public override int FrameCount { get; }
        public override int Width { get; }
        public override int Height { get; }

        public override void Dispose()
        {

        }
        public override Image<BGRA32> Read(int frame)
        {
            return null;
        }
    }
}
