using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;

using static BEditor.Drawing.Image;

namespace BEditor.Drawing.PixelOperation
{
    public readonly unsafe struct GrayscaleOperation : IPixelOperation
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;

        public GrayscaleOperation(BGRA32* src, BGRA32* dst)
        {
            _src = src;
            _dst = dst;
        }

        public readonly void Invoke(int pos)
        {
            var ntsc = Set255Round(
                (_src[pos].R * 0.11448) +
                (_src[pos].G * 0.58661) +
                (_src[pos].B * 0.29891));

            _dst[pos].B = (byte)ntsc;
            _dst[pos].G = (byte)ntsc;
            _dst[pos].R = (byte)ntsc;
            _dst[pos].A = _src[pos].A;
        }
    }
}