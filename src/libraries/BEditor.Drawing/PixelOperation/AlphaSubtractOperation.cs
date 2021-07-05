// AlphaSubtractOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Compute.Memory;
using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

namespace BEditor.Drawing
{
    /// <inheritdoc cref="Image"/>
    public static unsafe partial class Image
    {
        /// <summary>
        /// Subtracts Alpha values only.
        /// </summary>
        /// <param name="image">The image to be processed.</param>
        /// <param name="mask">The mask image.</param>
        /// <param name="context">When processing using Gpu, specify a valid DrawingContext.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="mask"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void AlphaSubtract(this Image<BGRA32> image, Image<BGRA32> mask, DrawingContext? context = null)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            if (mask is null) throw new ArgumentNullException(nameof(mask));
            image.ThrowIfDisposed();
            mask.ThrowIfDisposed();

            if (context is not null && !context.IsDisposed)
            {
                using var maskMem = context.Context.CreateMappingMemory(mask.Data, mask.DataSize);

                image.PixelOperate<AlphaSubtractOperation, AbstractMemory>(context, maskMem);
            }
            else
            {
                image.AlphaSubtractCpu(mask);
            }
        }

        private static void AlphaSubtractCpu(this Image<BGRA32> image, Image<BGRA32> mask)
        {
            fixed (BGRA32* data = image.Data)
            fixed (BGRA32* maskptr = mask.Data)
            {
                PixelOperate(image.Data.Length, new AlphaSubtractOperation(data, maskptr));
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Subtracts Alpha values only.
    /// </summary>
    public readonly unsafe struct AlphaSubtractOperation : IPixelOperation, IGpuPixelOperation<AbstractMemory>
    {
        private readonly BGRA32* _data;
        private readonly BGRA32* _mask;

        /// <summary>
        /// Initializes a new instance of the <see cref="AlphaSubtractOperation"/> struct.
        /// </summary>
        /// <param name="data">The image data.</param>
        /// <param name="mask">The mask image data.</param>
        public AlphaSubtractOperation(BGRA32* data, BGRA32* mask)
        {
            _data = data;
            _mask = mask;
        }

        /// <inheritdoc/>
        public string GetKernel()
        {
            return "alpha_sub";
        }

        /// <inheritdoc/>
        public string GetSource()
        {
            return @"
__kernel void alpha_sub(__global unsigned char* src, __global unsigned char* mask)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;

    src[pos + 3] = (unsigned char)(src[pos + 3] & mask[pos + 3]);
}";
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            _data[pos].A = (byte)(_data[pos].A & _mask[pos].A);
        }
    }
}