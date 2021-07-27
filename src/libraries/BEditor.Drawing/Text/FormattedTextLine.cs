// FormattedTextLine.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using SkiaSharp;

namespace BEditor.Drawing
{
    /// <summary>
    /// Stores information about a line of <see cref="FormattedText"/>.
    /// </summary>
    public class FormattedTextLine
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FormattedTextLine"/> class.
        /// </summary>
        /// <param name="text">The text in the line.</param>
        /// <param name="width">The width of the line, in pixels.</param>
        /// <param name="height">The height of the line, in pixels.</param>
        public FormattedTextLine(string text, float width, float height)
        {
            Text = text;
            Width = width;
            Height = height;
        }

        /// <summary>
        /// Gets the text in the line.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Gets the rectangles.
        /// </summary>
        // public RectangleF[] Rectangles { get; }

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
