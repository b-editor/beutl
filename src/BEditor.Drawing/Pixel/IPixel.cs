// IPixel.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Drawing.Pixel
{
    /// <summary>
    /// Represents a pixel that can be used in <see cref="Image{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of self pixel.</typeparam>
    public interface IPixel<T>
        where T : unmanaged, IPixel<T>
    {
        /// <summary>
        /// Blend this color with other colors.
        /// </summary>
        /// <param name="foreground">Other colors to blend with this color.</param>
        /// <returns>Returns the blended color.</returns>
        public T Blend(T foreground);

        /// <summary>
        /// Blend this color with other colors.
        /// </summary>
        /// <param name="foreground">Other colors to blend with this color.</param>
        /// <returns>Returns the blended color.</returns>
        public T Add(T foreground);

        /// <summary>
        /// Blend this color with other colors.
        /// </summary>
        /// <param name="foreground">Other colors to blend with this color.</param>
        /// <returns>Returns the blended color.</returns>
        public T Subtract(T foreground);
    }
}