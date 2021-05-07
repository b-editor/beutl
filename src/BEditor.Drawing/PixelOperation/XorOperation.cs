
using System;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

namespace BEditor.Drawing
{
    public static unsafe partial class Image
    {
        private static void XorCpu(this Image<BGRA32> image)
        {
            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new XorOperation(data, data));
            }
        }

        public static void Xor(this Image<BGRA32> image, DrawingContext? context = null)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            if (context is not null)
            {
                image.PixelOperate<XorOperation>(context);
            }
            else
            {
                image.XorCpu();
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    public readonly unsafe struct XorOperation : IPixelOperation, IGpuPixelOperation
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;

        public XorOperation(BGRA32* src, BGRA32* dst)
        {
            _src = src;
            _dst = dst;
        }

        public string GetKernel()
        {
            return "xor";
        }

        public string GetSource()
        {
            return @"
__kernel void xor(__global unsigned char* src)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;

    src[pos] = src[pos] ^ 128;
    src[pos + 1] = src[pos + 1] ^ 128;
    src[pos + 2] = src[pos + 2] ^ 128;
}";
        }

        public readonly void Invoke(int pos)
        {
            _dst[pos].B = (byte)(_src[pos].B ^ 128);
            _dst[pos].G = (byte)(_src[pos].G ^ 128);
            _dst[pos].R = (byte)(_src[pos].R ^ 128);
            _dst[pos].A = _src[pos].A;
        }
    }
}