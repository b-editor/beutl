// ContrastOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Adjusts the contrast of the pixels.
    /// </summary>
    public readonly unsafe struct ContrastOperation : IPixelOperation
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        private readonly byte* _lut;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContrastOperation"/> struct.
        /// </summary>
        /// <param name="src">The source image data.</param>
        /// <param name="dst">The destination image data.</param>
        /// <param name="lut">The look up table.</param>
        public ContrastOperation(BGRA32* src, BGRA32* dst, byte* lut)
        {
            _src = src;
            _dst = dst;
            _lut = lut;
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            _dst[pos].B = _lut[_src[pos].B];
            _dst[pos].G = _lut[_src[pos].G];
            _dst[pos].R = _lut[_src[pos].R];
            _dst[pos].A = _src[pos].A;
        }
    }
}