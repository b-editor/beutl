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
    public struct RGB24 : IPixel<RGB24>
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
        public RGB24 FromColor(Color color)
        {
            return new RGB24(color.R, color.G, color.B);
        }

        /// <inheritdoc/>
        public Color ToColor()
        {
            return this;
        }
    }
}