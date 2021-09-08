// FormattedTextLine.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Drawing
{
    /// <summary>
    /// Stores information about a line of <see cref="FormattedText"/>.
    /// </summary>
    public sealed class FormattedTextLine
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FormattedTextLine"/> class.
        /// </summary>
        /// <param name="text">The text in the line.</param>
        /// <param name="top">It's the position above this line.</param>
        /// <param name="width">The width of the line, in pixels.</param>
        /// <param name="height">The height of the line, in pixels.</param>
        public FormattedTextLine(string text, float top, float width, float height)
        {
            Text = text;
            Top = top;
            Width = width;
            Height = height;
        }

        /// <summary>
        /// Gets the text in the line.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Gets the position above this line.
        /// </summary>
        public float Top { get; }

        /// <summary>
        /// Gets the width of the line, in pixels.
        /// </summary>
        public float Width { get; }

        /// <summary>
        /// Gets the height of the line, in pixels.
        /// </summary>
        public float Height { get; }
    }
}