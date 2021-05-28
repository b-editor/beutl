// GammaOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Compute.Memory;
using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

namespace BEditor.Drawing
{
    /// <inheritdoc cref="Image"/>
    public static unsafe partial class Image
    {
        /// <summary>
        /// Adjusts the gamma of the image.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="gamma">The gamma. [range: 0.01-3.0].</param>
        /// <param name="context">When processing using Gpu, specify a valid DrawingContext.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Gamma(this Image<BGRA32> image, float gamma, DrawingContext? context = null)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();
            gamma = Math.Clamp(gamma, 0.01f, 3f);

            using var lut = new UnmanagedArray<byte>(256);
            for (var i = 0; i < 256; i++)
            {
                lut[i] = (byte)Set255Round(Math.Pow(i / 255.0, 1.0 / gamma) * 255);
            }

            if (context is not null && !context.IsDisposed)
            {
                using var lutMap = context.Context.CreateMappingMemory(lut.AsSpan(), lut.Length * sizeof(byte));

                image.PixelOperate<GammaOperation, AbstractMemory>(context, lutMap);
            }
            else
            {
                image.GammaCpu(lut);
            }
        }

        private static void GammaCpu(this Image<BGRA32> image, UnmanagedArray<byte> lut)
        {
            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new GammaOperation(data, data, (byte*)lut.Pointer));
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Adjusts the gamma of the pixels.
    /// </summary>
    public readonly unsafe struct GammaOperation : IPixelOperation, IGpuPixelOperation<AbstractMemory>
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        private readonly byte* _lut;

        /// <summary>
        /// Initializes a new instance of the <see cref="GammaOperation"/> struct.
        /// </summary>
        /// <param name="src">The source image data.</param>
        /// <param name="dst">The destination image data.</param>
        /// <param name="lut">The look up table.</param>
        public GammaOperation(BGRA32* src, BGRA32* dst, byte* lut)
        {
            _src = src;
            _dst = dst;
            _lut = lut;
        }

        /// <inheritdoc/>
        public string GetKernel()
        {
            return "gamma";
        }

        /// <inheritdoc/>
        public string GetSource()
        {
            return @"
__kernel void gamma(__global unsigned char* src, __global unsigned char* lut)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;

    src[pos] = lut[src[pos]];
    src[pos + 1] = lut[src[pos + 1]];
    src[pos + 2] = lut[src[pos + 2]];
}";
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            _dst[pos].B = _lut[_src[pos].B];
            _dst[pos].G = _lut[_src[pos].G];
            _dst[pos].R = _lut[_src[pos].R];
            _dst[pos].A = _src[pos].A;
        }
    }
}