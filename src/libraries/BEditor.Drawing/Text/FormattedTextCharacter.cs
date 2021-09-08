// FormattedTextCharacter.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing
{
    /// <summary>
    /// Represents a character in formatted text.
    /// </summary>
    public readonly struct FormattedTextCharacter
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FormattedTextCharacter"/> struct.
        /// </summary>
        /// <param name="image">The image.</param>
        /// <param name="rectangle">The rectangle.</param>
        public FormattedTextCharacter(Image<BGRA32> image, RectangleF rectangle)
        {
            Image = image;
            Rectangle = rectangle;
        }

        /// <summary>
        /// Gets the image.
        /// </summary>
        public Image<BGRA32> Image { get; }

        /// <summary>
        /// Gets the rectangle.
        /// </summary>
        public RectangleF Rectangle { get; }
    }
}