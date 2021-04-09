using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;

using static BEditor.Drawing.Image;

namespace BEditor.Drawing.Process
{
    internal readonly unsafe struct GrayscaleProcess
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;

        public GrayscaleProcess(BGRA32* src, BGRA32* dst)
        {
            _src = src;
            _dst = dst;
        }

        public void Invoke(int pos)
        {
            var ntsc = Set255Round(
                (_src[pos].B * 0.289) +
                (_src[pos].G * 0.586) +
                (_src[pos].R * 0.114));

            _dst[pos].B = (byte)ntsc;
            _dst[pos].G = (byte)ntsc;
            _dst[pos].R = (byte)ntsc;
        }
    }
}
