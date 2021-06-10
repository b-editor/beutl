// RGBColorOperation.cs
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
        /// Adjusts the RGB color tone.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="red">The red threshold value. [range: -255-255].</param>
        /// <param name="green">The green threshold value. [range: -255-255].</param>
        /// <param name="blue">The blue threshold value. [range: -255-255].</param>
        /// <param name="context">When processing using Gpu, specify a valid DrawingContext.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void RGBColor(this Image<BGRA32> image, short red, short green, short blue, DrawingContext? context = null)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();
            red = Math.Clamp(red, (short)-255, (short)255);
            green = Math.Clamp(green, (short)-255, (short)255);
            blue = Math.Clamp(blue, (short)-255, (short)255);

            if (context is not null && !context.IsDisposed)
            {
                image.PixelOperate<RGBColorOperation, short, short, short>(context, red, green, blue);
            }
            else
            {
                image.RGBColorCpu(red, green, blue);
            }
        }

        private static void RGBColorCpu(this Image<BGRA32> image, short red, short green, short blue)
        {
            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new RGBColorOperation(data, data, red, green, blue));
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Adjusts the RGB color tone.
    /// </summary>
    public readonly unsafe struct RGBColorOperation : IPixelOperation, IGpuPixelOperation<short, short, short>
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        private readonly short _r;
        private readonly short _g;
        private readonly short _b;

        /// <summary>
        /// Initializes a new instance of the <see cref="RGBColorOperation"/> struct.
        /// </summary>
        /// <param name="src">The source image data.</param>
        /// <param name="dst">The destination image data.</param>
        /// <param name="r">The red threshold value. [range: -255-255].</param>
        /// <param name="g">The green threshold value. [range: -255-255].</param>
        /// <param name="b">The blue threshold value. [range: -255-255].</param>
        public RGBColorOperation(BGRA32* src, BGRA32* dst, short r, short g, short b)
        {
            _src = src;
            _dst = dst;
            (_r, _g, _b) = (r, g, b);
        }

        /// <inheritdoc/>
        public string GetKernel()
        {
            return "rgbcolor";
        }

        /// <inheritdoc/>
        public string GetSource()
        {
            return @"
double set255(double value)
{
    if (value > 255) return 255;
    else if (value < 0) return 0;

    return value;
}

__kernel void rgbcolor(__global unsigned char* src, short r, short g, short b)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;

    src[pos] = (unsigned char)set255(src[pos] + b);
    src[pos + 1] = (unsigned char)set255(src[pos + 1] + g);
    src[pos + 2] = (unsigned char)set255(src[pos + 2] + r);
}";
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            _dst[pos].B = (byte)Set255(_src[pos].B + _b);
            _dst[pos].G = (byte)Set255(_src[pos].G + _g);
            _dst[pos].R = (byte)Set255(_src[pos].R + _r);
            _dst[pos].A = _src[pos].A;
        }
    }
}