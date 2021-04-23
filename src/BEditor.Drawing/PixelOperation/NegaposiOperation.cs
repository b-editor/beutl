using System;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

namespace BEditor.Drawing
{
    public static unsafe partial class Image
    {
        public static void Negaposi(this Image<BGRA32> image, byte red, byte green, byte blue)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new NegaposiOperation(data, data, red, green, blue));
            }
        }

        public static void Negaposi(this Image<BGRA32> image, DrawingContext context, byte red, byte green, byte blue)
        {
            image.PixelOperate<NegaposiOperation, byte, byte, byte>(context, red, green, blue);
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    public readonly unsafe struct NegaposiOperation : IPixelOperation, IGpuPixelOperation<byte, byte, byte>
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        private readonly byte _red;
        private readonly byte _green;
        private readonly byte _blue;

        public NegaposiOperation(BGRA32* src, BGRA32* dst, byte red, byte green, byte blue)
        {
            _src = src;
            _dst = dst;
            _red = red;
            _green = green;
            _blue = blue;
        }

        public string GetKernel()
        {
            return "negaposi";
        }

        public string GetSource()
        {
            return @"
__kernel void negaposi(__global unsigned char* src, unsigned char r, unsigned char g, unsigned char b)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;

    src[pos] = r - src[pos];
    src[pos + 1] = g - src[pos + 1];
    src[pos + 2] = b - src[pos + 2];
}";
        }

        public readonly void Invoke(int pos)
        {
            _dst[pos].B = (byte)(_red - _src[pos].B);
            _dst[pos].G = (byte)(_green - _src[pos].G);
            _dst[pos].R = (byte)(_blue - _src[pos].R);
            _dst[pos].A = _src[pos].A;
        }
    }
}