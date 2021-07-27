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
        /// <param name="startIndex">The index of the first character in the line, in characters.</param>
        /// <param name="length">The length of the line, in characters.</param>
        /// <param name="rectangles">The rectangles.</param>
        public FormattedTextLine(int startIndex, int length, RectangleF[] rectangles)
        {
            StartIndex = startIndex;
            Length = length;
            Rectangles = rectangles;
        }

        /// <summary>
        /// Gets the length of the line, in characters.
        /// </summary>
        public int StartIndex { get; }

        /// <summary>
        /// Gets the length of the line, in characters.
        /// </summary>
        public int Length { get; }

        /// <summary>
        /// Gets the rectangles.
        /// </summary>
        public RectangleF[] Rectangles { get; }

        /// <summary>
        /// Gets the height of the line, in pixels.
        /// </summary>
        public float Width { get; }

        /// <summary>
        /// Gets the height of the line, in pixels.
        /// </summary>
        public float Height { get; }
    }
}
