// NegaposiOperation.cs
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
        /// Reverses the image to negative or positive.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="red">The red threshold value.</param>
        /// <param name="green">The green threshold value.</param>
        /// <param name="blue">The blue threshold value.</param>
        /// <param name="context">When processing using Gpu, specify a valid DrawingContext.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Negaposi(this Image<BGRA32> image, byte red, byte green, byte blue, DrawingContext? context = null)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            if (context is not null && !context.IsDisposed)
            {
                image.PixelOperate<NegaposiOperation, byte, byte, byte>(context, red, green, blue);
            }
            else
            {
                image.NegaposiCpu(red, green, blue);
            }
        }

        private static void NegaposiCpu(this Image<BGRA32> image, byte red, byte green, byte blue)
        {
            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new NegaposiOperation(data, data, red, green, blue));
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Reverses the pixels to negative or positive.
    /// </summary>
    public readonly unsafe struct NegaposiOperation : IPixelOperation, IGpuPixelOperation<byte, byte, byte>
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        private readonly byte _red;
        private readonly byte _green;
        private readonly byte _blue;

        /// <summary>
        /// Initializes a new instance of the <see cref="NegaposiOperation"/> struct.
        /// </summary>
        /// <param name="src">The source image data.</param>
        /// <param name="dst">The destination image data.</param>
        /// <param name="red">The red threshold value.</param>
        /// <param name="green">The green threshold value.</param>
        /// <param name="blue">The blue threshold value.</param>
        public NegaposiOperation(BGRA32* src, BGRA32* dst, byte red, byte green, byte blue)
        {
            _src = src;
            _dst = dst;
            _red = red;
            _green = green;
            _blue = blue;
        }

        /// <inheritdoc/>
        public string GetKernel()
        {
            return "negaposi";
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            _dst[pos].B = (byte)(_red - _src[pos].B);
            _dst[pos].G = (byte)(_green - _src[pos].G);
            _dst[pos].R = (byte)(_blue - _src[pos].R);
            _dst[pos].A = _src[pos].A;
        }
    }
}