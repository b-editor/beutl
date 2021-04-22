using System;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

using static BEditor.Drawing.Image;

namespace BEditor.Drawing
{
    public static unsafe partial class Image
    {
        public static void RGBColor(this Image<BGRA32> image, short red, short green, short blue)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();
            red = Math.Clamp(red, (short)-255, (short)255);
            green = Math.Clamp(green, (short)-255, (short)255);
            blue = Math.Clamp(blue, (short)-255, (short)255);

            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new RGBColorOperation(data, data, red, green, blue));
            }
        }

        public static void RGBColor(this Image<BGRA32> image, DrawingContext context, short red, short green, short blue)
        {
            red = Math.Clamp(red, (short)-255, (short)255);
            green = Math.Clamp(green, (short)-255, (short)255);
            blue = Math.Clamp(blue, (short)-255, (short)255);

            image.PixelOperate<RGBColorOperation, short, short, short>(context, red, green, blue);
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    public readonly unsafe struct RGBColorOperation : IPixelOperation, IGpuPixelOperation<short, short, short>
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        private readonly short _r;
        private readonly short _g;
        private readonly short _b;

        public RGBColorOperation(BGRA32* src, BGRA32* dst, short r, short g, short b)
        {
            _src = src;
            _dst = dst;
            (_r, _g, _b) = (r, g, b);
        }

        public string GetKernel()
        {
            return "rgbcolor";
        }

        public string GetSource()
        {
            return @"
double set255(double value)
{
    if (value > 255) return 255;
    else if (value < 0) return 0;

    return value;
}

__kernel void rgbcolor(__global unsigned char* src, short r, short g, short b)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;

    src[pos] = (unsigned char)set255(src[pos] + b);
    src[pos + 1] = (unsigned char)set255(src[pos + 1] + g);
    src[pos + 2] = (unsigned char)set255(src[pos + 2] + r);
}";
        }

        public readonly void Invoke(int pos)
        {
            _dst[pos].B = (byte)Set255(_src[pos].B + _b);
            _dst[pos].G = (byte)Set255(_src[pos].G + _g);
            _dst[pos].R = (byte)Set255(_src[pos].R + _r);
            _dst[pos].A = _src[pos].A;
        }
    }
}