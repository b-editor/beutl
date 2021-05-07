using System;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

namespace BEditor.Drawing
{
    public static unsafe partial class Image
    {
        private static void BinarizationCpu(this Image<BGRA32> image, byte value)
        {
            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new BinarizationOperation(data, data, value));
            }
        }

        public static void Binarization(this Image<BGRA32> image, byte value, DrawingContext? context = null)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            if (context is not null)
            {
                image.PixelOperate<BinarizationOperation, byte>(context, value);
            }
            else
            {
                image.BinarizationCpu(value);
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    public readonly unsafe struct BinarizationOperation : IPixelOperation, IGpuPixelOperation<byte>
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        private readonly byte _value;

        public BinarizationOperation(BGRA32* src, BGRA32* dst, byte value)
        {
            _src = src;
            _dst = dst;
            _value = value;
        }

        public string GetKernel()
        {
            return "binarization";
        }

        public string GetSource()
        {
            return @"
__kernel void binarization(__global unsigned char* src, unsigned char value)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;

    if (src[pos] <= value &&
        src[pos + 1] <= value &&
        src[pos + 2] <= value)
    {
        src[pos] = src[pos + 1] = src[pos + 2] = src[pos + 3] = 0;
    }
    else
    {
        src[pos] = src[pos + 1] = src[pos + 2] = src[pos + 3] = 255;
    }
}";
        }

        public readonly void Invoke(int pos)
        {
            if (_src[pos].R <= _value &&
                _src[pos].G <= _value &&
                _src[pos].B <= _value)
            {
                _dst[pos].R = _dst[pos].G = _dst[pos].B = _dst[pos].A = 0;
            }
            else
            {
                _dst[pos].R = _dst[pos].G = _dst[pos].B = _dst[pos].A = 255;
            }
        }
    }
}