// BGRA32.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Runtime.InteropServices;

namespace BEditor.Drawing.Pixel
{
    /// <summary>
    /// Represents the 32-bit BGRA pixel.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    [PixelFormat(4)]
    public struct BGRA32 : IPixel<BGRA32>, IGpuPixel<BGRA32>, IPixelConvertable<BGR24>, IPixelConvertable<RGB24>, IPixelConvertable<RGBA32>
    {
        /// <summary>
        /// The blue component.
        /// </summary>
        public byte B;

        /// <summary>
        /// The green component.
        /// </summary>
        public byte G;

        /// <summary>
        /// The red component.
        /// </summary>
        public byte R;

        /// <summary>
        /// The alpha component.
        /// </summary>
        public byte A;

        /// <summary>
        /// Initializes a new instance of the <see cref="BGRA32"/> struct.
        /// </summary>
        /// <param name="r">The red component.</param>
        /// <param name="g">The green component.</param>
        /// <param name="b">The blue component.</param>
        /// <param name="a">The alpha component.</param>
        public BGRA32(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        /// <inheritdoc/>
        public readonly BGRA32 Add(BGRA32 foreground)
        {
            return new(
                (byte)(R + foreground.R),
                (byte)(G + foreground.G),
                (byte)(B + foreground.B),
                (byte)(A + foreground.A));
        }

        /// <inheritdoc/>
        public readonly BGRA32 Blend(BGRA32 mask)
        {
            if (mask.A is 0) return this;

            var dst = default(BGRA32);

            var blendA = (mask.A + A) - (mask.A * A / 255);

            dst.B = (byte)(((mask.B * mask.A) + (B * (255 - mask.A) * A / 255)) / blendA);
            dst.G = (byte)(((mask.G * mask.A) + (G * (255 - mask.A) * A / 255)) / blendA);
            dst.R = (byte)(((mask.R * mask.A) + (R * (255 - mask.A) * A / 255)) / blendA);
            dst.A = A;

            return dst;
        }

        /// <inheritdoc/>
        public readonly BGRA32 Subtract(BGRA32 foreground)
        {
            return new(
                (byte)(R - foreground.R),
                (byte)(G - foreground.G),
                (byte)(B - foreground.B),
                (byte)(A - foreground.A));
        }

        /// <inheritdoc/>
        public void ConvertFrom(BGR24 src)
        {
            B = src.B;
            G = src.G;
            R = src.R;
            A = 255;
        }

        /// <inheritdoc/>
        public void ConvertFrom(RGBA32 src)
        {
            B = src.B;
            G = src.G;
            R = src.R;
            A = src.A;
        }

        /// <inheritdoc/>
        public void ConvertFrom(RGB24 src)
        {
            B = src.B;
            G = src.G;
            R = src.R;
            A = 255;
        }

        /// <inheritdoc/>
        public readonly void ConvertTo(out BGR24 dst)
        {
            dst = new(R, G, B);
        }

        /// <inheritdoc/>
        public readonly void ConvertTo(out RGB24 dst)
        {
            dst = new(R, G, B);
        }

        /// <inheritdoc/>
        public readonly void ConvertTo(out RGBA32 dst)
        {
            dst = new(R, G, B, A);
        }

        /// <inheritdoc/>
        public string GetBlend()
        {
            return @"
__kernel void blend(__global unsigned char* src, __global unsigned char* mask)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;

    if (mask[pos + 3] == 0) return;

    unsigned char b = src[pos];
    unsigned char g = src[pos + 1];
    unsigned char r = src[pos + 2];
    unsigned char a = src[pos + 3];
    unsigned char mask_b = mask[pos];
    unsigned char mask_g = mask[pos + 1];
    unsigned char mask_r = mask[pos + 2];
    unsigned char mask_a = mask[pos + 3];

    int blendA = (mask_a + a) - (mask_a * a / 255);

    src[pos] = (unsigned char)(((mask_b * mask_a) + (b * (255 - mask_a) * a / 255)) / blendA);
    src[pos + 1] = (unsigned char)(((mask_g * mask_a) + (g * (255 - mask_a) * a / 255)) / blendA);
    src[pos + 2] = (unsigned char)(((mask_r * mask_a) + (r * (255 - mask_a) * a / 255)) / blendA);
    src[pos + 3] = a;
}";
        }

        /// <inheritdoc/>
        public string GetAdd()
        {
            return @"
__kernel void add(__global unsigned char* src, __global unsigned char* mask)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;

    src[pos] += mask[pos];
    src[pos + 1] += mask[pos + 1];
    src[pos + 2] += mask[pos + 2];
    src[pos + 3] += mask[pos + 3];
}";
        }

        /// <inheritdoc/>
        public string Subtract()
        {
            return @"
__kernel void subtract(__global unsigned char* src, __global unsigned char* mask)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;

    src[pos] -= mask[pos];
    src[pos + 1] -= mask[pos + 1];
    src[pos + 2] -= mask[pos + 2];
    src[pos + 3] -= mask[pos + 3];
}";
        }
    }
}