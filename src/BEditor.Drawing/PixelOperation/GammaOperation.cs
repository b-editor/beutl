
using System;

using BEditor.Compute.Memory;
using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

namespace BEditor.Drawing
{
    public static unsafe partial class Image
    {
        private static void GammaCpu(this Image<BGRA32> image, UnmanagedArray<byte> lut)
        {
            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new GammaOperation(data, data, (byte*)lut.Pointer));
            }
        }

        public static void Gamma(this Image<BGRA32> image, float gamma, DrawingContext? context = null)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();
            gamma = Math.Clamp(gamma, 0.01f, 3f);

            using var lut = new UnmanagedArray<byte>(256);
            for (var i = 0; i < 256; i++)
            {
                lut[i] = (byte)Set255Round(Math.Pow(i / 255.0, 1.0 / gamma) * 255);
            }

            if (context is not null)
            {
                using var lutMap = context.Context.CreateMappingMemory(lut.AsSpan(), lut.Length * sizeof(byte));

                image.PixelOperate<GammaOperation, AbstractMemory>(context, lutMap);
            }
            else
            {
                image.GammaCpu(lut);
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    public readonly unsafe struct GammaOperation : IPixelOperation, IGpuPixelOperation<AbstractMemory>
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        private readonly byte* _lut;

        public GammaOperation(BGRA32* src, BGRA32* dst, byte* lut)
        {
            _src = src;
            _dst = dst;
            _lut = lut;
        }

        public string GetKernel()
        {
            return "gamma";
        }

        public string GetSource()
        {
            return @"
__kernel void gamma(__global unsigned char* src, __global unsigned char* lut)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;

    src[pos] = lut[src[pos]];
    src[pos + 1] = lut[src[pos + 1]];
    src[pos + 2] = lut[src[pos + 2]];
}";
        }

        public readonly void Invoke(int pos)
        {
            _dst[pos].B = _lut[_src[pos].B];
            _dst[pos].G = _lut[_src[pos].G];
            _dst[pos].R = _lut[_src[pos].R];
            _dst[pos].A = _src[pos].A;
        }
    }
}