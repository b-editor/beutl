
using System;

using BEditor.Compute.Memory;
using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

namespace BEditor.Drawing
{
    public static unsafe partial class Image
    {
        public static void AlphaSubtract(this Image<BGRA32> image, Image<BGRA32> mask)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            fixed (BGRA32* data = image.Data)
            fixed (BGRA32* maskptr = mask.Data)
            {
                PixelOperate(image.Data.Length, new AlphaSubtractOperation(data, maskptr));
            }
        }

        public static void AlphaSubtract(this Image<BGRA32> image, Image<BGRA32> mask, DrawingContext context)
        {
            using var maskMem = context.Context.CreateMappingMemory(mask.Data, mask.DataSize);

            image.PixelOperate<AlphaSubtractOperation, AbstractMemory>(context, maskMem);
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    public readonly unsafe struct AlphaSubtractOperation : IPixelOperation, IGpuPixelOperation<AbstractMemory>
    {
        private readonly BGRA32* _data;
        private readonly BGRA32* _mask;

        public AlphaSubtractOperation(BGRA32* data, BGRA32* mask)
        {
            _data = data;
            _mask = mask;
        }

        public string GetKernel()
        {
            return "alpha_sub";
        }

        public string GetSource()
        {
            return @"
__kernel void alpha_sub(__global unsigned char* src, __global unsigned char* mask)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;

    src[pos + 3] -= mask[pos + 3];
}";
        }

        public readonly void Invoke(int pos)
        {
            //_data[pos].A -= _mask[pos].A;
            _data[pos].A = (byte)((_data[pos].A - _mask[pos].A) + (_mask[pos].A * _data[pos].A));
        }
    }
}