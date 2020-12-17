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
        public readonly void Convert(out BGRA32 dst)
        {
            dst = new(R, G, B, 255);
        }
        public readonly void Convert(out BGR24 dst)
        {
            dst = new(R, G, B);
        }
        public readonly void Convert(out RGBA32 dst)
        {
            dst = new(R, G, B, 255);
        }
    }
}
