// SepiaOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

using static BEditor.Drawing.Image;

namespace BEditor.Drawing
{
    /// <inheritdoc cref="Image"/>
    public static unsafe partial class Image
    {
        /// <summary>
        /// Make the image sepia-toned.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="context">When processing using Gpu, specify a valid DrawingContext.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Sepia(this Image<BGRA32> image, DrawingContext? context = null)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            if (context is not null && !context.IsDisposed)
            {
                image.PixelOperate<SepiaOperation>(context);
            }
            else
            {
                image.SepiaCpu();
            }
        }

        private static void SepiaCpu(this Image<BGRA32> image)
        {
            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new SepiaOperation(data, data));
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Make the pixels sepia-toned.
    /// </summary>
    public readonly unsafe struct SepiaOperation : IPixelOperation, IGpuPixelOperation
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;

        /// <summary>
        /// Initializes a new instance of the <see cref="SepiaOperation"/> struct.
        /// </summary>
        /// <param name="src">The source image data.</param>
        /// <param name="dst">The destination image data.</param>
        public SepiaOperation(BGRA32* src, BGRA32* dst)
        {
            _src = src;
            _dst = dst;
        }

        /// <inheritdoc/>
        public string GetKernel()
        {
            return "sepia";
        }

        /// <inheritdoc/>
        public string GetSource()
        {
            return @"
double set255Round(double value)
{
    if (value > 255) return 255;
    else if (value < 0) return 0;

    return round(value);
}
double set255(double value)
{
    if (value > 255) return 255;
    else if (value < 0) return 0;

    return value;
}

__kernel void sepia(__global unsigned char* src)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;

    double ntsc = set255Round(
        (src[pos + 2] * 0.11448) +
        (src[pos + 1] * 0.58661) +
        (src[pos] * 0.29891));

    src[pos] = (unsigned char)set255(ntsc - 20);
    src[pos + 1] = (unsigned char)ntsc;
    src[pos + 2] = (unsigned char)set255(ntsc + 30);
}";
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            var ntsc = Set255Round(
                (_src[pos].R * 0.11448) +
                (_src[pos].G * 0.58661) +
                (_src[pos].B * 0.29891));

            _dst[pos].B = (byte)Set255(ntsc - 20);
            _dst[pos].G = (byte)ntsc;
            _dst[pos].R = (byte)Set255(ntsc + 30);
            _dst[pos].A = _src[pos].A;
        }
    }
}