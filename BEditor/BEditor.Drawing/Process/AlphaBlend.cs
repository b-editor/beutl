using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.Process
{
    internal unsafe readonly struct AlphaBlendProcess
    {
        private readonly BGRA32* dst;
        private readonly BGRA32* src;

        public AlphaBlendProcess(BGRA32* dst, BGRA32* src)
        {
            this.dst = dst;
            this.src = src;
        }

        public readonly void Invoke(int pos)
        {
            var srcA = src[pos].A;

            if (srcA is 0) return;

            var dstA = dst[pos].A;
            var blendA = (srcA + dstA) - srcA * dstA / 255;

            dst[pos].B = (byte)((src[pos].B * srcA + dst[pos].B * (255 - srcA) * dstA / 255) / blendA);
            dst[pos].G = (byte)((src[pos].G * srcA + dst[pos].G * (255 - srcA) * dstA / 255) / blendA);
            dst[pos].R = (byte)((src[pos].R * srcA + dst[pos].R * (255 - srcA) * dstA / 255) / blendA);
        }
    }
}
