using System;
using System.Runtime.InteropServices;

namespace BEditor.Drawing.Pixel
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    [PixelFormat(3)]
    public struct RGB24 : IPixel<RGB24>, IPixelConvertable<BGRA32>, IPixelConvertable<BGR24>, IPixelConvertable<RGBA32>
    {
        public byte R;
        public byte G;
        public byte B;

        public RGB24(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public readonly RGB24 Blend(RGB24 foreground) => foreground;
        public void ConvertFrom(BGRA32 src)
        {
            R = src.R;
            G = src.G;
            B = src.B;
        }
        public void ConvertFrom(BGR24 src)
        {
            R = src.R;
            G = src.G;
            B = src.B;
        }
        public void ConvertFrom(RGBA32 src)
        {
            R = src.R;
            G = src.G;
            B = src.B;
        }
        public readonly void ConvertTo(out BGRA32 dst)
        {
            dst = new(R, G, B, 255);
        }
        public readonly void ConvertTo(out BGR24 dst)
        {
            dst = new(R, G, B);
        }
        public readonly void ConvertTo(out RGBA32 dst)
        {
            dst = new(R, G, B, 255);
        }
    }
}
