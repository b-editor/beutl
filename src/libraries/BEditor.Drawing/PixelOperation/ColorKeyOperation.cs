// ColorKeyOperation.cs
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
        /// Makes a specific color component of the image transparent.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="color">The color to make transparent.</param>
        /// <param name="range">The color difference range.</param>
        /// <param name="context">When processing using Gpu, specify a valid DrawingContext.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void ColorKey(this Image<BGRA32> image, BGRA32 color, int range, DrawingContext? context = null)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            if (context?.IsDisposed == false)
            {
                var colorNtsc = Set255Round(
                    (color.R * 0.11448) +
                    (color.G * 0.58661) +
                    (color.B * 0.29891));
                image.PixelOperate<ColorKeyOperation, double, int>(context, colorNtsc, range);
            }
            else
            {
                image.ColorKeyCpu(color, range);
            }
        }

        private static void ColorKeyCpu(this Image<BGRA32> image, BGRA32 color, int range)
        {
            fixed (BGRA32* s = image.Data)
            {
                PixelOperate(image.Data.Length, new ColorKeyOperation(s, s, color, range));
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Makes a specific color component of the image transparent.
    /// </summary>
    public unsafe readonly struct ColorKeyOperation : IPixelOperation, IGpuPixelOperation<double, int>
    {
        private readonly BGRA32* _dst;
        private readonly BGRA32* _src;
        private readonly double _colorNtsc;
        private readonly int _value;

        /// <summary>
        /// Initializes a new instance of the <see cref="ColorKeyOperation"/> struct.
        /// </summary>
        /// <param name="src">The source image data.</param>
        /// <param name="dst">The destination image data.</param>
        /// <param name="color">The color to make transparent.</param>
        /// <param name="value">The threshold value.</param>
        public ColorKeyOperation(BGRA32* src, BGRA32* dst, BGRA32 color, int value)
        {
            _dst = dst;
            _src = src;
            _value = value;

            _colorNtsc = Set255Round(
                (color.R * 0.11448) +
                (color.G * 0.58661) +
                (color.B * 0.29891));
        }

        /// <inheritdoc/>
        public string GetKernel()
        {
            return "colorkey";
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

__kernel void colorkey(__global unsigned char* src, double colorNtsc, int range)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;
    unsigned char r = src[pos + 2];
    unsigned char g = src[pos + 1];
    unsigned char b = src[pos];
    double ntsc = set255Round(
        (r * 0.11448) +
        (g * 0.58661) +
        (b * 0.29891));

    if (abs((long)(colorNtsc - ntsc)) < range)
    {
        src[pos + 3] = 0;
    }
}";
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            var camColor = _src[pos];
            var ntsc = Set255Round(
                (camColor.R * 0.11448) +
                (camColor.G * 0.58661) +
                (camColor.B * 0.29891));

            if (Math.Abs(_colorNtsc - ntsc) < _value)
            {
                camColor = default;
            }

            _dst[pos] = camColor;
        }
    }
}