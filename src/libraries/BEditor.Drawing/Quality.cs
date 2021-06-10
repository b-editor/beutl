// Quality.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Drawing
{
    /// <summary>
    /// Indicates the quality used in <see cref="Image.Resize(Image{Pixel.BGRA32}, int, int, Quality)"/>.
    /// </summary>
    public enum Quality
    {
        /// <summary>
        /// Unspecified.
        /// </summary>
        None,

        /// <summary>
        /// Low quality.
        /// </summary>
        Low,

        /// <summary>
        /// Medium quality.
        /// </summary>
        Medium,

        /// <summary>
        /// High quality.
        /// </summary>
        High,
    }
}