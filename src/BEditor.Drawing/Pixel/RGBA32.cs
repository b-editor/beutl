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

        public readonly RGBA32 Add(RGBA32 foreground)
        {
            return new(
                (byte)(R + foreground.R),
                (byte)(G + foreground.G),
                (byte)(B + foreground.B),
                (byte)(A + foreground.A));
        }

        public readonly RGBA32 Blend(RGBA32 mask)
        {
            if (mask.A is 0) return this;

            var dst = new RGBA32();

            var blendA = mask.A + A - (mask.A * A / 255);

            dst.B = (byte)(((mask.B * mask.A) + (B * (255 - mask.A) * A / 255)) / blendA);
            dst.G = (byte)(((mask.G * mask.A) + (G * (255 - mask.A) * A / 255)) / blendA);
            dst.R = (byte)(((mask.R * mask.A) + (R * (255 - mask.A) * A / 255)) / blendA);
            dst.A = A;

            return dst;
        }

        public readonly RGBA32 Subtract(RGBA32 foreground)
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