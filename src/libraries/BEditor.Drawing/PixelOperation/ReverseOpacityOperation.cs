// ReverseOpacityOperation.cs
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
        /// Reverses the opacity of an image.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="context">When processing using Gpu, specify a valid DrawingContext.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void ReverseOpacity(this Image<BGRA32> image, DrawingContext? context = null)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            if (context is not null && !context.IsDisposed)
            {
                image.PixelOperate<ReverseOpacityOperation>(context);
            }
            else
            {
                image.ReverseOpacityCpu();
            }
        }

        private static void ReverseOpacityCpu(this Image<BGRA32> image)
        {
            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new ReverseOpacityOperation(data, data));
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Reverses the opacity of the pixels.
    /// </summary>
    public readonly unsafe struct ReverseOpacityOperation : IPixelOperation, IGpuPixelOperation
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReverseOpacityOperation"/> struct.
        /// </summary>
        /// <param name="src">The source image data.</param>
        /// <param name="dst">The destination image data.</param>
        public ReverseOpacityOperation(BGRA32* src, BGRA32* dst)
        {
            _src = src;
            _dst = dst;
        }

        /// <inheritdoc/>
        public string GetKernel()
        {
            return "reverse_opacity";
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public void Invoke(int pos)
        {
            var src = _src[pos];
            src.A = (byte)(255 - src.A);

            _dst[pos] = src;
        }
    }
}