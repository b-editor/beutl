using System;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

using static BEditor.Drawing.Image;

namespace BEditor.Drawing
{
    public static unsafe partial class Image
    {
        public static void Brightness(this Image<BGRA32> image, short brightness)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();
            brightness = Math.Clamp(brightness, (short)-255, (short)255);

            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new BrightnessOperation(data, data, brightness));
            }
        }

        public static void Brightness(this Image<BGRA32> image, DrawingContext context, short brightness)
        {
            brightness = Math.Clamp(brightness, (short)-255, (short)255);
            image.PixelOperate<BrightnessOperation, short>(context, brightness);
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    public readonly unsafe struct BrightnessOperation : IPixelOperation, IGpuPixelOperation<short>
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        // -255 - 255
        private readonly short _light;

        public BrightnessOperation(BGRA32* src, BGRA32* dst, short light)
        {
            _src = src;
            _dst = dst;
            _light = light;
        }

        public string GetKernel()
        {
            return "brightness";
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

__kernel void brightness(__global unsigned char* src, short light)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;

    src[pos] = (unsigned char)set255(src[pos] + light);
    src[pos + 1] = (unsigned char)set255(src[pos + 1] + light);
    src[pos + 2] = (unsigned char)set255(src[pos + 2] + light);
}";
        }

        public readonly void Invoke(int pos)
        {
            _dst[pos].B = (byte)Set255(_src[pos].B + _light);
            _dst[pos].G = (byte)Set255(_src[pos].G + _light);
            _dst[pos].R = (byte)Set255(_src[pos].R + _light);
            _dst[pos].A = _src[pos].A;
        }
    }
}