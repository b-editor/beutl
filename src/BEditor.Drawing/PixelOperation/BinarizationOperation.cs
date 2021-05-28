// BinarizationOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

namespace BEditor.Drawing
{
    /// <inheritdoc cref="Image"/>
    public static unsafe partial class Image
    {
        /// <summary>
        /// Binarizes the image.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="value">The threshold value.</param>
        /// <param name="context">When processing using Gpu, specify a valid DrawingContext.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Binarization(this Image<BGRA32> image, byte value, DrawingContext? context = null)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            if (context is not null && !context.IsDisposed)
            {
                image.PixelOperate<BinarizationOperation, byte>(context, value);
            }
            else
            {
                image.BinarizationCpu(value);
            }
        }

        private static void BinarizationCpu(this Image<BGRA32> image, byte value)
        {
            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new BinarizationOperation(data, data, value));
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Binarizes the pixels.
    /// </summary>
    public readonly unsafe struct BinarizationOperation : IPixelOperation, IGpuPixelOperation<byte>
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        private readonly byte _value;

        /// <summary>
        /// Initializes a new instance of the <see cref="BinarizationOperation"/> struct.
        /// </summary>
        /// <param name="src">The source image data.</param>
        /// <param name="dst">The destination image data.</param>
        /// <param name="value">The threshold value.</param>
        public BinarizationOperation(BGRA32* src, BGRA32* dst, byte value)
        {
            _src = src;
            _dst = dst;
            _value = value;
        }

        /// <inheritdoc/>
        public string GetKernel()
        {
            return "binarization";
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
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