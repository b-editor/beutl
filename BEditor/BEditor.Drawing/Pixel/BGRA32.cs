using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Drawing.Pixel
{
    [StructLayout(LayoutKind.Sequential)]
    [PixelFormat(4, (0 & (DepthMax - 1)) + ((ch - 1) << ChannelShift))]
    public struct BGRA32 : IPixel<BGRA32>
    {
        public byte B;
        public byte G;
        public byte R;
        public byte A;

        internal const int ch = 4;
        internal const int ChannelMax = 512,
            ChannelShift = 3,
            DepthMax = 1 << ChannelShift;

        public readonly BGRA32 Blend(BGRA32 foreground)
        {
            if (A is 0) return this;

            var result = new BGRA32();

            var dstA = foreground.A;
            var blendA = (A + dstA) - A * dstA / 255;

            result.B = (byte)((B * A + foreground.B * (255 - A) * dstA / 255) / blendA);
            result.G = (byte)((G * A + foreground.G * (255 - A) * dstA / 255) / blendA);
            result.R = (byte)((R * A + foreground.R * (255 - A) * dstA / 255) / blendA);

            return result;
        }
    }
}
