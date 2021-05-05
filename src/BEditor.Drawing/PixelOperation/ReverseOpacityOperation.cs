using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

namespace BEditor.Drawing
{
    public static unsafe partial class Image
    {
        public static void ReverseOpacity(this Image<BGRA32> image)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new ReverseOpacityOperation(data, data));
            }
        }

        public static void ReverseOpacity(this Image<BGRA32> image, DrawingContext context)
        {
            image.PixelOperate<ReverseOpacityOperation>(context);
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    public readonly unsafe struct ReverseOpacityOperation : IPixelOperation, IGpuPixelOperation
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;

        public ReverseOpacityOperation(BGRA32* src, BGRA32* dst)
        {
            _src = src;
            _dst = dst;
        }

        public string GetKernel()
        {
            return "reverse_opacity";
        }

        public string GetSource()
        {
            return @"
__kernel void reverse_opacity(__global unsigned char* src)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;

    src[pos + 3] = (unsigned char)(255 - src[pos + 3]);
}";
        }

        public void Invoke(int pos)
        {
            var src = _src[pos];
            src.A = (byte)(255 - src.A);

            _dst[pos] = src;
        }
    }
}
