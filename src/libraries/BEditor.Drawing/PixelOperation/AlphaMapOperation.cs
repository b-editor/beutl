// AlphaMapOperation.cs
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
        /// Expand the BGRA image into the Alpha channel.
        /// </summary>
        /// <param name="image">The image to convert.</param>
        /// <returns>Returns the alpha map.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static Image<Grayscale8> AlphaMap(this Image<BGRA32> image)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();
            var result = new Image<Grayscale8>(image.Width, image.Height);
            fixed (BGRA32* src = image.Data)
            fixed (Grayscale8* dst = result.Data)
            {
                PixelOperate(image.Data.Length, new AlphaMapOperation(src, dst));
            }

            return result;
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Expand the BGRA image into the Alpha channel.
    /// </summary>
    public readonly unsafe struct AlphaMapOperation : IPixelOperation
    {
        private readonly BGRA32* _src;
        private readonly Grayscale8* _dst;

        /// <summary>
        /// Initializes a new instance of the <see cref="AlphaMapOperation"/> struct.
        /// </summary>
        /// <param name="src">The source.</param>
        /// <param name="dst">The destination.</param>
        public AlphaMapOperation(BGRA32* src, Grayscale8* dst)
        {
            _src = src;
            _dst = dst;
        }

        /// <inheritdoc/>
        public void Invoke(int pos)
        {
            _dst[pos] = new(_src[pos].A);
        }
    }
}