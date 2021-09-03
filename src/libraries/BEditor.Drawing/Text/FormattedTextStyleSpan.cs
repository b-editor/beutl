// FormattedTextStyleSpan.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Drawing
{
    /// <summary>
    /// Describes the formatting for a span of text in a <see cref="FormattedText"/> object.
    /// </summary>
    public readonly struct FormattedTextStyleSpan
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FormattedTextStyleSpan"/> struct.
        /// </summary>
        /// <param name="lineNumber">The number of lines of the target.</param>
        /// <param name="range">The range of the span.</param>
        /// <param name="foregroundBrush">The span's foreground brush.</param>
        /// <param name="strokeColor">The stroke color.</param>
        public FormattedTextStyleSpan(int lineNumber, Range range, Color foregroundBrush, Color strokeColor = default)
        {
            LineNumber = lineNumber;
            Range = range;
            ForegroundBrush = foregroundBrush;
            StrokeColor = strokeColor;
        }

        /// <summary>
        /// Gets the number of lines of the target.
        /// </summary>
        public int LineNumber { get; }

        /// <summary>
        /// Gets the range of the span.
        /// </summary>
        public Range Range { get; }

        /// <summary>
        /// Gets the span's foreground brush.
        /// </summary>
        public Color ForegroundBrush { get; }

        /// <summary>
        /// Gets the stroke color.
        /// </summary>
        public Color StrokeColor { get; }
    }
}
