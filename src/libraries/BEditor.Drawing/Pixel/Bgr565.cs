// Bgr565.cs
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
    /// Represents the 16-bit BGR pixel.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    [PixelFormat(3)]
    public struct Bgr565 : IPixel<Bgr565>
    {
        /// <summary>
        /// The value.
        /// </summary>
        public ushort Value;

        /// <summary>
        /// Initializes a new instance of the <see cref="Bgr565"/> struct.
        /// </summary>
        /// <param name="value">The value.</param>
        public Bgr565(ushort value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public Bgr565 Add(Bgr565 foreground)
        {
            throw new Exception();
        }

        /// <inheritdoc/>
        public Bgr565 Blend(Bgr565 foreground)
        {
            throw new Exception();
        }

        /// <inheritdoc/>
        public Bgr565 FromColor(Color color)
        {
            var b = (color.B >> 3) & 0x1f;
            var g = ((color.G >> 2) & 0x3f) << 5;
            var r = ((color.R >> 3) & 0x1f) << 11;

            return new Bgr565((ushort)(r | g | b));
        }

        /// <inheritdoc/>
        public Bgr565 Subtract(Bgr565 foreground)
        {
            throw new Exception();
        }

        /// <inheritdoc/>
        public Color ToColor()
        {
            var b = (Value & 0x1f) << 3;
            var g = ((Value >> 5) & 0x3f) << 2;
            var r = ((Value >> 11) & 0x1f) << 3;

            return Color.FromArgb(255, (byte)r, (byte)g, (byte)b);
        }
    }
}
