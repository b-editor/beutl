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
        public void ConvertFrom(BGR24 src)
        {
            B = src.B;
            G = src.G;
            R = src.R;
            A = 255;
        }
        public void ConvertFrom(RGB24 src)
        {
            B = src.B;
            G = src.G;
            R = src.R;
            A = 255;
        }
        public void ConvertFrom(RGBA32 src)
        {
            B = src.B;
            G = src.G;
            R = src.R;
            A = src.A;
        }
        public readonly void ConvertTo(out BGR24 dst)
        {
            dst = new(R, G, B);
        }
        public readonly void ConvertTo(out RGB24 dst)
        {
            dst = new(R, G, B);
        }
        public readonly void ConvertTo(out RGBA32 dst)
        {
            dst = new(R, G, B, A);
        }
    }
}
