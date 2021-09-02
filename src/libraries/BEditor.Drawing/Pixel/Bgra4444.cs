// Bgra4444.cs
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
    /// Represents the 16-bit BGRA pixel.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    [PixelFormat(4)]
    public struct Bgra4444 : IPixel<Bgra4444>
    {
        /// <summary>
        /// The value.
        /// </summary>
        public ushort Value;

        /// <summary>
        /// Initializes a new instance of the <see cref="Bgra4444"/> struct.
        /// </summary>
        /// <param name="value">The value.</param>
        public Bgra4444(ushort value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public Bgra4444 Add(Bgra4444 foreground)
        {
            throw new Exception();
        }

        /// <inheritdoc/>
        public Bgra4444 Blend(Bgra4444 foreground)
        {
            throw new Exception();
        }

        /// <inheritdoc/>
        public Bgra4444 FromColor(Color color)
        {
            return new Bgra4444(
                (ushort)((((int)Math.Round((color.A / 255F) * 15F) & 0x0F) << 12)
                | (((int)Math.Round((color.R / 255F) * 15F) & 0x0F) << 8)
                | (((int)Math.Round((color.G / 255F) * 15F) & 0x0F) << 4)
                | ((int)Math.Round((color.B / 255F) * 15F) & 0x0F)));
        }

        /// <inheritdoc/>
        public Bgra4444 Subtract(Bgra4444 foreground)
        {
            throw new Exception();
        }

        /// <inheritdoc/>
        public Color ToColor()
        {
            const float Max = 15F;

            return Color.FromArgb(
                (byte)(((Value >> 12) & 0x0F) * Max),
                (byte)(((Value >> 8) & 0x0F) * Max),
                (byte)(((Value >> 4) & 0x0F) * Max),
                (byte)((Value & 0x0F) * Max));
        }
    }
}
