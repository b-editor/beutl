// RGB24.cs
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
    /// Represents the 24-bit RGB pixel.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    [PixelFormat(3)]
    public struct RGB24 : IPixel<RGB24>, IPixelConvertable<BGRA32>, IPixelConvertable<BGR24>, IPixelConvertable<RGBA32>
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
        /// Initializes a new instance of the <see cref="RGB24"/> struct.
        /// </summary>
        /// <param name="r">The red component.</param>
        /// <param name="g">The green component.</param>
        /// <param name="b">The blue component.</param>
        public RGB24(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        /// <inheritdoc/>
        public readonly RGB24 Add(RGB24 foreground)
        {
            return new(
                (byte)(R + foreground.R),
                (byte)(G + foreground.G),
                (byte)(B + foreground.B));
        }

        /// <inheritdoc/>
        public readonly RGB24 Blend(RGB24 foreground)
        {
            return foreground;
        }

        /// <inheritdoc/>
        public readonly RGB24 Subtract(RGB24 foreground)
        {
            return new(
                (byte)(R - foreground.R),
                (byte)(G - foreground.G),
                (byte)(B - foreground.B));
        }

        /// <inheritdoc/>
        public void ConvertFrom(BGRA32 src)
        {
            R = src.R;
            G = src.G;
            B = src.B;
        }

        /// <inheritdoc/>
        public void ConvertFrom(BGR24 src)
        {
            R = src.R;
            G = src.G;
            B = src.B;
        }

        /// <inheritdoc/>
        public void ConvertFrom(RGBA32 src)
        {
            R = src.R;
            G = src.G;
            B = src.B;
        }

        /// <inheritdoc/>
        public readonly void ConvertTo(out BGRA32 dst)
        {
            dst = new(R, G, B, 255);
        }

        /// <inheritdoc/>
        public readonly void ConvertTo(out BGR24 dst)
        {
            dst = new(R, G, B);
        }

        /// <inheritdoc/>
        public readonly void ConvertTo(out RGBA32 dst)
        {
            dst = new(R, G, B, 255);
        }
    }
}