using System;
using System.Runtime.InteropServices;

namespace BEditor.Drawing.Pixel
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    [PixelFormat(4)]
    public struct RGBA32 : IPixel<RGBA32>, IPixelConvertable<BGR24>, IPixelConvertable<RGB24>, IPixelConvertable<RGBA32>
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A;

        public RGBA32(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public readonly RGBA32 Blend(RGBA32 foreground)
        {
            if (A is 0) return this;

            var result = new RGBA32();

            var dstA = foreground.A;
            var blendA = (A + dstA) - A * dstA / 255;

            result.B = (byte)((B * A + foreground.B * (255 - A) * dstA / 255) / blendA);
            result.G = (byte)((G * A + foreground.G * (255 - A) * dstA / 255) / blendA);
            result.R = (byte)((R * A + foreground.R * (255 - A) * dstA / 255) / blendA);

            return result;
        }
        public readonly void Convert(out BGR24 dst)
        {
            dst = new(R, G, B);
        }
        public readonly void Convert(out RGB24 dst)
        {
            dst = new(R, G, B);
        }
        public readonly void Convert(out RGBA32 dst)
        {
            dst = new(R, G, B, A);
        }
    }
}
