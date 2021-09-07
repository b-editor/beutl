// RGBA32.cs
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
    /// Represents the 32-bit RGBA pixel.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    [PixelFormat(4)]
    public struct RGBA32 : IPixel<RGBA32>
    {
        /// <summary>
        /// The red component.
        /// </summary>
        public byte R;

        /// <summary>
        /// The green component.
        /// </summary>
        public byte G;

        /// <summary>
        /// The blue component.
        /// </summary>
        public byte B;

        /// <summary>
        /// The alpha component.
        /// </summary>
        public byte A;

        /// <summary>
        /// Initializes a new instance of the <see cref="RGBA32"/> struct.
        /// </summary>
        /// <param name="r">The red component.</param>
        /// <param name="g">The green component.</param>
        /// <param name="b">The blue component.</param>
        /// <param name="a">The alpha component.</param>
        public RGBA32(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        /// <inheritdoc/>
        public readonly RGBA32 Add(RGBA32 foreground)
        {
            return new(
                (byte)(R + foreground.R),
                (byte)(G + foreground.G),
                (byte)(B + foreground.B),
                (byte)(A + foreground.A));
        }

        /// <inheritdoc/>
        public readonly RGBA32 Blend(RGBA32 mask)
        {
            if (mask.A is 0) return this;

            var dst = default(RGBA32);

            var blendA = mask.A + A - (mask.A * A / 255);

            dst.B = (byte)(((mask.B * mask.A) + (B * (255 - mask.A) * A / 255)) / blendA);
            dst.G = (byte)(((mask.G * mask.A) + (G * (255 - mask.A) * A / 255)) / blendA);
            dst.R = (byte)(((mask.R * mask.A) + (R * (255 - mask.A) * A / 255)) / blendA);
            dst.A = A;

            return dst;
        }

        /// <inheritdoc/>
        public readonly RGBA32 Subtract(RGBA32 foreground)
        {
            return new(
                (byte)(R - foreground.R),
                (byte)(G - foreground.G),
                (byte)(B - foreground.B),
                (byte)(A - foreground.A));
        }

        /// <inheritdoc/>
        public RGBA32 FromColor(Color color)
        {
            return new RGBA32(color.R, color.G, color.B, color.A);
        }

        /// <inheritdoc/>
        public Color ToColor()
        {
            return this;
        }
    }
}