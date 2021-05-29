// ChromaKeyOperation.cs
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
        /// Makes the green color transparent.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="value">The threshold value.</param>
        /// <param name="context">When processing using Gpu, specify a valid DrawingContext.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void ChromaKey(this Image<BGRA32> image, int value, DrawingContext? context = null)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            if (context is not null && !context.IsDisposed)
            {
                image.PixelOperate<ChromaKeyOperation, int>(context, value);
            }
            else
            {
                image.ChromaKeyCpu(value);
            }
        }

        private static void ChromaKeyCpu(this Image<BGRA32> image, int value)
        {
            fixed (BGRA32* s = image.Data)
            {
                PixelOperate(image.Data.Length, new ChromaKeyOperation(s, s, value));
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Makes the green color transparent.
    /// </summary>
    public unsafe readonly struct ChromaKeyOperation : IPixelOperation, IGpuPixelOperation<int>
    {
        private readonly BGRA32* _dst;
        private readonly BGRA32* _src;
        private readonly int _value;

        /// <summary>
        /// Initializes a new instance of the <see cref="ChromaKeyOperation"/> struct.
        /// </summary>
        /// <param name="src">The source image data.</param>
        /// <param name="dst">The destination image data.</param>
        /// <param name="value">The threshold value.</param>
        public ChromaKeyOperation(BGRA32* src, BGRA32* dst, int value)
        {
            _dst = dst;
            _src = src;
            _value = value;
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
__kernel void chromakey(__global unsigned char* src, int value)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;
    unsigned char r = src[pos + 2];
    unsigned char g = src[pos + 1];
    unsigned char b = src[pos];
    
    unsigned char maxi = max(max(r, g), b);
    unsigned char mini = min(min(r, g), b);

    if (g != mini
        && (g == maxi
        || maxi - g < 8)
        && (maxi - mini) > value)
    {
        src[pos + 3] = 0;
    }
}";
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            var camColor = _src[pos];

            var max = Math.Max(Math.Max(camColor.R, camColor.G), camColor.B);
            var min = Math.Min(Math.Min(camColor.R, camColor.G), camColor.B);

            var replace =
                camColor.G != min
                && (camColor.G == max
                || max - camColor.G < 8)
                && (max - min) > _value;

            if (replace)
            {
                camColor = default;
            }

            _dst[pos] = camColor;
        }
    }
}