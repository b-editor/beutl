using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

using static BEditor.Drawing.Image;

namespace BEditor.Drawing
{
    public static unsafe partial class Image
    {
        public static void Grayscale(this Image<BGRA32> image)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new GrayscaleOperation(data, data));
            }
        }

        public static void Grayscale(this Image<BGRA32> image, DrawingContext context)
        {
            image.PixelOperate<GrayscaleOperation>(context);
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    public readonly unsafe struct GrayscaleOperation : IPixelOperation, IGpuPixelOperation
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;

        public GrayscaleOperation(BGRA32* src, BGRA32* dst)
        {
            _src = src;
            _dst = dst;
        }

        public string GetKernel()
        {
            return "grayscale";
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

__kernel void grayscale(__global unsigned char* src)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;

    double ntsc = set255Round(
        (src[pos + 2] * 0.11448) +
        (src[pos + 1] * 0.58661) +
        (src[pos] * 0.29891));

    src[pos] = (unsigned char)ntsc;
    src[pos + 1] = (unsigned char)ntsc;
    src[pos + 2] = (unsigned char)ntsc;
}";
        }

        public readonly void Invoke(int pos)
        {
            var ntsc = Set255Round(
                (_src[pos].R * 0.11448) +
                (_src[pos].G * 0.58661) +
                (_src[pos].B * 0.29891));

            _dst[pos].B = (byte)ntsc;
            _dst[pos].G = (byte)ntsc;
            _dst[pos].R = (byte)ntsc;
            _dst[pos].A = _src[pos].A;
        }
    }
}