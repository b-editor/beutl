// BGR24.cs
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
    /// Represents the 24-bit BGR pixel.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    [PixelFormat(3)]
    public struct BGR24 : IPixel<BGR24>
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
        /// Initializes a new instance of the <see cref="BGR24"/> struct.
        /// </summary>
        /// <param name="r">The red component.</param>
        /// <param name="g">The green component.</param>
        /// <param name="b">The blue component.</param>
        public BGR24(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        /// <inheritdoc/>
        public readonly BGR24 Add(BGR24 foreground)
        {
            return new(
                (byte)(R + foreground.R),
                (byte)(G + foreground.G),
                (byte)(B + foreground.B));
        }

        /// <inheritdoc/>
        public readonly BGR24 Blend(BGR24 foreground) => foreground;

        /// <inheritdoc/>
        public readonly BGR24 Subtract(BGR24 foreground)
        {
            return new(
                (byte)(R - foreground.R),
                (byte)(G - foreground.G),
                (byte)(B - foreground.B));
        }

        /// <inheritdoc/>
        public BGR24 FromColor(Color color)
        {
            return new BGR24(color.R, color.G, color.B);
        }

        /// <inheritdoc/>
        public Color ToColor()
        {
            return this;
        }
    }
}