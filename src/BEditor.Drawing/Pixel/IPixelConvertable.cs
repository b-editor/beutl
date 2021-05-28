// IPixelConvertable.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Drawing.Pixel
{
    /// <summary>
    /// Provides the ability to convert pixels.
    /// </summary>
    /// <typeparam name="T">The type of the pixel to convert.</typeparam>
    public interface IPixelConvertable<T>
        where T : unmanaged, IPixel<T>
    {
        /// <summary>
        /// Convert to <typeparamref name="T"/>.
        /// </summary>
        /// <param name="dst">Converted pixel.</param>
        public void ConvertTo(out T dst);

        /// <summary>
        /// Convert from <typeparamref name="T"/> to this pixel.
        /// </summary>
        /// <param name="src">The pixel to convert.</param>
        public void ConvertFrom(T src);
    }
}