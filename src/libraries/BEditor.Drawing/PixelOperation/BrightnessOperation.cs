// BrightnessOperation.cs
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
        /// Adjusts the brightness of the image.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="brightness">The brightness. [range: -255-255].</param>
        /// <param name="context">When processing using Gpu, specify a valid DrawingContext.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Brightness(this Image<BGRA32> image, short brightness, DrawingContext? context = null)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();
            brightness = Math.Clamp(brightness, (short)-255, (short)255);

            if (context is not null && !context.IsDisposed)
            {
                image.PixelOperate<BrightnessOperation, short>(context, brightness);
            }
            else
            {
                image.BrightnessCpu(brightness);
            }
        }

        private static void BrightnessCpu(this Image<BGRA32> image, short brightness)
        {
            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new BrightnessOperation(data, data, brightness));
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Adjusts the brightness of the pixels.
    /// </summary>
    public readonly unsafe struct BrightnessOperation : IPixelOperation, IGpuPixelOperation<short>
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        private readonly short _brightness;

        /// <summary>
        /// Initializes a new instance of the <see cref="BrightnessOperation"/> struct.
        /// </summary>
        /// <param name="src">The source image data.</param>
        /// <param name="dst">The destination image data.</param>
        /// <param name="brightness">The brightness.</param>
        public BrightnessOperation(BGRA32* src, BGRA32* dst, short brightness)
        {
            _src = src;
            _dst = dst;
            _brightness = brightness;
        }

        /// <inheritdoc/>
        public string GetKernel()
        {
            return "brightness";
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

__kernel void brightness(__global unsigned char* src, short light)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;

    src[pos] = (unsigned char)set255(src[pos] + light);
    src[pos + 1] = (unsigned char)set255(src[pos + 1] + light);
    src[pos + 2] = (unsigned char)set255(src[pos + 2] + light);
}";
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            _dst[pos].B = (byte)Set255(_src[pos].B + _brightness);
            _dst[pos].G = (byte)Set255(_src[pos].G + _brightness);
            _dst[pos].R = (byte)Set255(_src[pos].R + _brightness);
            _dst[pos].A = _src[pos].A;
        }
    }
}