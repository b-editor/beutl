using System;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

using static BEditor.Drawing.Image;

namespace BEditor.Drawing
{
    public static unsafe partial class Image
    {
        private static void SepiaCpu(this Image<BGRA32> image)
        {
            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new SepiaOperation(data, data));
            }
        }

        public static void Sepia(this Image<BGRA32> image, DrawingContext? context = null)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            if (context is not null)
            {
                image.PixelOperate<SepiaOperation>(context);
            }
            else
            {
                image.SepiaCpu();
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    public readonly unsafe struct SepiaOperation : IPixelOperation, IGpuPixelOperation
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;

        public SepiaOperation(BGRA32* src, BGRA32* dst)
        {
            _src = src;
            _dst = dst;
        }

        public string GetKernel()
        {
            return "sepia";
        }

        public string GetSource()
        {
            return @"
double set255Round(double value)
{
    if (value > 255) return 255;
    else if (value < 0) return 0;

    return round(value);
}
double set255(double value)
{
    if (value > 255) return 255;
    else if (value < 0) return 0;

    return value;
}

__kernel void sepia(__global unsigned char* src)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;

    double ntsc = set255Round(
        (src[pos + 2] * 0.11448) +
        (src[pos + 1] * 0.58661) +
        (src[pos] * 0.29891));

    src[pos] = (unsigned char)set255(ntsc - 20);
    src[pos + 1] = (unsigned char)ntsc;
    src[pos + 2] = (unsigned char)set255(ntsc + 30);
}";
        }

        public readonly void Invoke(int pos)
        {
            var ntsc = Set255Round(
                (_src[pos].R * 0.11448) +
                (_src[pos].G * 0.58661) +
                (_src[pos].B * 0.29891));

            _dst[pos].B = (byte)Set255(ntsc - 20);
            _dst[pos].G = (byte)ntsc;
            _dst[pos].R = (byte)Set255(ntsc + 30);
            _dst[pos].A = _src[pos].A;
        }
    }
}