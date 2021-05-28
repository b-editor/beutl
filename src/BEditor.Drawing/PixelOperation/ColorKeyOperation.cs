// ColorKeyOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Makes a specific color component of the image transparent.
    /// </summary>
    public unsafe readonly struct ColorKeyOperation : IPixelOperation
    {
        private readonly BGRA32* _dst;
        private readonly BGRA32* _src;
        private readonly BGRA32 _color;
        private readonly int _value;

        /// <summary>
        /// Initializes a new instance of the <see cref="ColorKeyOperation"/> struct.
        /// </summary>
        /// <param name="src">The source image data.</param>
        /// <param name="dst">The destination image data.</param>
        /// <param name="color">The color to make transparent.</param>
        /// <param name="value">The threshold value.</param>
        public ColorKeyOperation(BGRA32* src, BGRA32* dst, BGRA32 color, int value)
        {
            _dst = dst;
            _src = src;
            _color = color;
            _value = value;
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            var camColor = _src[pos];

            if (Math.Abs(_color.R - camColor.R) < _value
                && Math.Abs(_color.G - camColor.G) < _value
                && Math.Abs(_color.B - camColor.B) < _value)
            {
                camColor = default;
            }

            _dst[pos] = camColor;
        }
    }
}