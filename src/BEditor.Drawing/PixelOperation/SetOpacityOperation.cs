// SetOpacityOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Sets the opacity of the pixels.
    /// </summary>
    public readonly unsafe struct SetOpacityOperation : IPixelOperation
    {
        private readonly BGRA32* _data;
        private readonly float _opacity;

        /// <summary>
        /// Initializes a new instance of the <see cref="SetOpacityOperation"/> struct.
        /// </summary>
        /// <param name="data">The image data.</param>
        /// <param name="opacity">The opacity of the image. [range: 0.0-1.0].</param>
        public SetOpacityOperation(BGRA32* data, float opacity)
        {
            _data = data;
            _opacity = opacity;
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            _data[pos].A = (byte)(_data[pos].A * _opacity);
        }
    }
}