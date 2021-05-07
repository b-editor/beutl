
using System;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

namespace BEditor.Drawing
{
    public static unsafe partial class Image
    {
        private static void ChromaKeyCpu(this Image<BGRA32> image, int value)
        {
            fixed (BGRA32* s = image.Data)
            {
                PixelOperate(image.Data.Length, new ChromaKeyOperation(s, s, value));
            }
        }

        public static void ChromaKey(this Image<BGRA32> image, int value, DrawingContext? context = null)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            if (context is not null)
            {
                image.PixelOperate<ChromaKeyOperation, int>(context, value);
            }
            else
            {
                image.ChromaKeyCpu(value);
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    public unsafe readonly struct ChromaKeyOperation : IPixelOperation, IGpuPixelOperation<int>
    {
        private readonly BGRA32* _dst;
        private readonly BGRA32* _src;
        private readonly int _value;

        public ChromaKeyOperation(BGRA32* src, BGRA32* dst, int value)
        {
            _dst = dst;
            _src = src;
            _value = value;
        }

        public string GetKernel()
        {
            return "chromakey";
        }

        public string GetSource()
        {
            return @"
__kernel void chromakey(__global unsigned char* src, int value)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;
    unsigned char r = src[pos + 2];
    unsigned char g = src[pos + 1];
    unsigned char b = src[pos];
    
    unsigned char maxi = max(max(r, g), b);
    unsigned char mini = min(min(r, g), b);

    if (g != mini
        && (g == maxi
        || maxi - g < 8)
        && (maxi - mini) > value)
    {
        src[pos + 3] = 0;
    }
}";
        }

        public readonly void Invoke(int pos)
        {
            var camColor = _src[pos];

            var max = Math.Max(Math.Max(camColor.R, camColor.G), camColor.B);
            var min = Math.Min(Math.Min(camColor.R, camColor.G), camColor.B);

            var replace =
                camColor.G != min
                && (camColor.G == max
                || max - camColor.G < 8)
                && (max - min) > _value;

            if (replace)
            {
                camColor = default;
            }

            _dst[pos] = camColor;
        }
    }
}