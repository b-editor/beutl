// ChromaKeyOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Runtime.InteropServices;

using BEditor.Compute;
using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

using static BEditor.Drawing.PixelOperation.ChromaKeyOperation;

namespace BEditor.Drawing
{
    /// <inheritdoc cref="Image"/>
    public static unsafe partial class Image
    {
        /// <summary>
        /// Makes the green color transparent.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="color">The color to make transparent.</param>
        /// <param name="hueRange">The hue range.</param>
        /// <param name="satRange">The saturation range.</param>
        /// <param name="context">When processing using Gpu, specify a valid DrawingContext.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void ChromaKey(this Image<BGRA32> image, BGRA32 color, int hueRange, int satRange, DrawingContext? context = null)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            if (context?.IsDisposed == false)
            {
                var hsv = ((Color)color).ToHsv();
                image.PixelOperate<ChromaKeyOperation, Double2, int, int>(
                    context,
                    new Double2(hsv.H, hsv.S),
                    hueRange,
                    satRange);
            }
            else
            {
                image.ChromaKeyCpu(color, hueRange, satRange);
            }
        }

        private static void ChromaKeyCpu(this Image<BGRA32> image, BGRA32 color, int hueRange, int satRange)
        {
            fixed (BGRA32* s = image.Data)
            {
                PixelOperate(image.Data.Length, new ChromaKeyOperation(s, s, color, hueRange, satRange));
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Makes the specific color transparent.
    /// </summary>
    public unsafe readonly struct ChromaKeyOperation : IPixelOperation, IGpuPixelOperation<Double2, int, int>
    {
        private readonly BGRA32* _dst;
        private readonly BGRA32* _src;
        private readonly Hsv _color;
        private readonly int _satRange;
        private readonly int _hueRange;

        /// <summary>
        /// Initializes a new instance of the <see cref="ChromaKeyOperation"/> struct.
        /// </summary>
        /// <param name="src">The source image data.</param>
        /// <param name="dst">The destination image data.</param>
        /// <param name="color">The color to make transparent.</param>
        /// <param name="hueRange">The hue range.</param>
        /// <param name="satRange">The saturation range.</param>
        public ChromaKeyOperation(BGRA32* src, BGRA32* dst, BGRA32 color, int hueRange, int satRange)
        {
            _dst = dst;
            _src = src;
            _color = ((Color)color).ToHsv();
            _satRange = satRange;
            _hueRange = hueRange;
        }

        /// <inheritdoc/>
        public string GetKernel()
        {
            return "chromakey";
        }

        /// <inheritdoc/>
        public string GetSource()
        {
            return @"
double2 toHsv(uchar r, uchar g, uchar b)
{
    double hue;
    double sat;
    uchar maxv = max(max(r, g), b);
    uchar minv = min(min(r, g), b);

    if (maxv != minv)
    {
        // hue
        if (maxv == r) { hue = 60 * (g - b) / (maxv - minv); }
        if (maxv == g) { hue = (60 * (b - r) / (maxv - minv)) + 120; }
        if (maxv == b) { hue = (60 * (r - g) / (maxv - minv)) + 240; }

        // saturation
        sat = (maxv - minv) / maxv;
    }

    if (hue < 0)
    {
        hue += 360;
    }

    hue = round(hue);
    sat = round(sat * 100);
    return (double2)(hue, sat);
}

__kernel void chromakey(__global unsigned char* src, double2 color, int hueRange, int satRange)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;
    unsigned char r = src[pos + 2];
    unsigned char g = src[pos + 1];
    unsigned char b = src[pos];
    double2 hs = toHsv(r, g, b);

    if (abs((long)(color.x - hs.x)) < hueRange
        && abs((long)(color.y - hs.y)) < satRange)
    {
        src[pos + 3] = 0;
    }
}";
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            var camColor = _src[pos];
            var srcHsv = ((Color)camColor).ToHsv();

            if (Math.Abs(_color.H - srcHsv.H) < _hueRange
                && Math.Abs(_color.S - srcHsv.S) < _satRange)
            {
                camColor = default;
            }

            _dst[pos] = camColor;
        }
    }
}