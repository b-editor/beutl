// CropOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.RowOperation
{
    /// <summary>
    /// Crops the image to the specified area.
    /// </summary>
    /// <typeparam name="TPixel">The type of pixel.</typeparam>
    public readonly unsafe struct CropOperation<TPixel> : IRowOperation
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly Image<TPixel> _src;
        private readonly Image<TPixel> _dst;
        private readonly Rectangle _roi;

        /// <summary>
        /// Initializes a new instance of the <see cref="CropOperation{TPixel}"/> struct.
        /// </summary>
        /// <param name="src">The source image.</param>
        /// <param name="dst">The destination image. Same size as <paramref name="roi"/>.</param>
        /// <param name="roi">The area to crop the image.</param>
        public CropOperation(Image<TPixel> src, Image<TPixel> dst, Rectangle roi)
        {
            _src = src;
            _dst = dst;
            _roi = roi;
        }

        /// <inheritdoc/>
        public readonly void Invoke(int y)
        {
            var sourceRow = _src[y + _roi.Y].Slice(_roi.X, _roi.Width);
            var targetRow = _dst[y];

            sourceRow.Slice(0, _roi.Width).CopyTo(targetRow);
        }
    }
}