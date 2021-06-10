// SetColorOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Sets the color of the pixels.
    /// </summary>
    public readonly unsafe struct SetColorOperation : IPixelOperation
    {
        private readonly BGRA32* _data;
        private readonly BGRA32 _color;

        /// <summary>
        /// Initializes a new instance of the <see cref="SetColorOperation"/> struct.
        /// </summary>
        /// <param name="data">The image data.</param>
        /// <param name="color">The color of the image.</param>
        public SetColorOperation(BGRA32* data, BGRA32 color)
        {
            _data = data;
            _color = color;
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            _data[pos].B = _color.B;
            _data[pos].G = _color.G;
            _data[pos].R = _color.R;
        }
    }
}