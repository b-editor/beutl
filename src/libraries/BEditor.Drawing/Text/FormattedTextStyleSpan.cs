// FormattedTextStyleSpan.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

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
        /// <param name="startIndex">The index of the first character in the span.</param>
        /// <param name="length">The length of the span.</param>
        /// <param name="foregroundBrush">The span's foreground brush.</param>
        public FormattedTextStyleSpan(
            int startIndex,
            int length,
            Color foregroundBrush)
        {
            StartIndex = startIndex;
            Length = length;
            ForegroundBrush = foregroundBrush;
        }

        /// <summary>
        /// Gets the index of the first character in the span.
        /// </summary>
        public int StartIndex { get; }

        /// <summary>
        /// Gets the length of the span.
        /// </summary>
        public int Length { get; }

        /// <summary>
        /// Gets the span's foreground brush.
        /// </summary>
        public Color ForegroundBrush { get; }
    }
}
