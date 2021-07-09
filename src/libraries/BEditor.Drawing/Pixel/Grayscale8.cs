// Grayscale8.cs
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
    /// Represents the 8-bit grayscale.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    [PixelFormat(1)]
    public struct Grayscale8 : IPixel<Grayscale8>
    {
        /// <summary>
        /// The value.
        /// </summary>
        public byte Value;

        /// <summary>
        /// Initializes a new instance of the <see cref="Grayscale8"/> struct.
        /// </summary>
        /// <param name="value">The value.</param>
        public Grayscale8(byte value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public Grayscale8 Add(Grayscale8 foreground)
        {
            return new((byte)(Value + foreground.Value));
        }

        /// <inheritdoc/>
        public Grayscale8 Blend(Grayscale8 foreground)
        {
            return new((byte)(Value + foreground.Value));
        }

        /// <inheritdoc/>
        public Grayscale8 Subtract(Grayscale8 foreground)
        {
            return new((byte)(Value - foreground.Value));
        }
    }
}