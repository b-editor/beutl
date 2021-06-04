// ApplyLookupTableOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

namespace BEditor.Drawing
{
    /// <inheritdoc cref="Image"/>
    public static unsafe partial class Image
    {
        /// <summary>
        /// Adjusts the gamma of the image.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="lut">The lookup table.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="lut"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void ApplyLookupTable(this Image<BGRA32> image, LookupTable lut)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            if (lut is null) throw new ArgumentNullException(nameof(lut));
            image.ThrowIfDisposed();

            fixed (BGRA32* data = image.Data)
            {
                PixelOperate(image.Data.Length, new ApplyLookupTableOperation(data, data, (byte*)lut.GetPointer()));
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Apply the Lookup Table.
    /// </summary>
    public readonly unsafe struct ApplyLookupTableOperation : IPixelOperation
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        private readonly byte* _lut;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplyLookupTableOperation"/> struct.
        /// </summary>
        /// <param name="src">The source image data.</param>
        /// <param name="dst">The destination image data.</param>
        /// <param name="lut">The lookup table.</param>
        public ApplyLookupTableOperation(BGRA32* src, BGRA32* dst, byte* lut)
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
