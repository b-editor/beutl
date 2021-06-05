// ContrastOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing
{
    /// <inheritdoc cref="Image"/>
    public static unsafe partial class Image
    {
        /// <summary>
        /// Adjusts the contrast of the image.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="contrast">The contrast [range: -255-255].</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Contrast(this Image<BGRA32> image, short contrast)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            using var lut = LookupTable.Contrast(contrast);
            ApplyLookupTable(image, lut);
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Creates a lookup table to adjust the contrast.
    /// </summary>
    public readonly unsafe struct ContrastOperation : IPixelOperation
    {
        private readonly short _contrast;
        private readonly float* _lut;

        /// <summary>
        /// Initializes a new instance of the <see cref="ContrastOperation"/> struct.
        /// </summary>
        /// <param name="contrast">The contrast [range: -255-255].</param>
        /// <param name="lut">The lookup table.</param>
        public ContrastOperation(short contrast, float* lut)
        {
            _contrast = contrast;
            _lut = lut;
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            _lut[pos] = Image.Set255Round(((1f + (_contrast / 255f)) * (pos - 128f)) + 128f) / 255f;
        }
    }
}