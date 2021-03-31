using System;
using System.Runtime.InteropServices;

namespace BEditor.Drawing.Pixel
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    [PixelFormat(4)]
    public struct BGRA32 : IPixel<BGRA32>, IPixelConvertable<BGR24>, IPixelConvertable<RGB24>, IPixelConvertable<RGBA32>
    {
        public byte B;
        public byte G;
        public byte R;
        public byte A;

        public BGRA32(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public readonly BGRA32 Add(BGRA32 foreground)
        {
            return new(
                (byte)(R + foreground.R),
                (byte)(G + foreground.G),
                (byte)(B + foreground.B),
                (byte)(A + foreground.A));
        }
        public readonly BGRA32 Blend(BGRA32 mask)
        {
            if (mask.A is 0) return this;

            var dst = new BGRA32();

            var blendA = (mask.A + A) - mask.A * A / 255;

            dst.B = (byte)((mask.B * mask.A + B * (255 - mask.A) * A / 255) / blendA);
            dst.G = (byte)((mask.G * mask.A + G * (255 - mask.A) * A / 255) / blendA);
            dst.R = (byte)((mask.R * mask.A + R * (255 - mask.A) * A / 255) / blendA);
            dst.A = A;

            return dst;
        }
        public readonly BGRA32 Subtract(BGRA32 foreground)
        {
            return new(
                (byte)(R - foreground.R),
                (byte)(G - foreground.G),
                (byte)(B - foreground.B),
                (byte)(A - foreground.A));
        }
        public void ConvertFrom(BGR24 src)
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
        public void ConvertFrom(RGB24 src)
        {
            B = src.B;
            G = src.G;
            R = src.R;
            A = 255;
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
        public void Deconstruct(out byte r, out byte g, out byte b, out byte a)
        {
            r = R;
            g = G;
            b = B;
            a = A;
        }
    }
}
