// XorOperation.cs
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
        /// XOR color conversion.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="context">When processing using Gpu, specify a valid DrawingContext.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Xor(this Image<BGRA32> image, DrawingContext? context = null)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            if (context is not null && !context.IsDisposed)
            {
                image.PixelOperate<XorOperation>(context);
            }
            else
            {
                image.XorCpu();
            }
        }

        private static void XorCpu(this Image<BGRA32> image)
        {
            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new XorOperation(data, data));
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// XOR color conversion.
    /// </summary>
    public readonly unsafe struct XorOperation : IPixelOperation, IGpuPixelOperation
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;

        /// <summary>
        /// Initializes a new instance of the <see cref="XorOperation"/> struct.
        /// </summary>
        /// <param name="src">The source image data.</param>
        /// <param name="dst">The destination image data.</param>
        public XorOperation(BGRA32* src, BGRA32* dst)
        {
            _src = src;
            _dst = dst;
        }

        /// <inheritdoc/>
        public string GetKernel()
        {
            return "xor";
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            _dst[pos].B = (byte)(_src[pos].B ^ 128);
            _dst[pos].G = (byte)(_src[pos].G ^ 128);
            _dst[pos].R = (byte)(_src[pos].R ^ 128);
            _dst[pos].A = _src[pos].A;
        }
    }
}